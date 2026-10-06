using Astro.Nina;
using Astro.Server.Engine;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Tasks.Wrap;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Astro.Server.Tests.Real;

/// <summary>
/// 실장비 코드(NinaRig 등)를 N.I.N.A. 시뮬레이터 장비에 붙여 확인한다 — 실장비가 없을 때 미리 (2026-10-06).
/// N.I.N.A.를 실험용 프로필(AstroAssistant)로 바꾸고 Simulator Camera · Telescope Simulator for .NET을 연결한 뒤
/// 환경 변수 AA_SIM=1과 함께 돌린다. 평소에는 아무것도 하지 않고 통과한다. 실제 장비 프로필에서는 돌리지 않는다(적도의가 움직임)
/// </summary>
public class SimulatorRigTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AA_SIM") == "1";

    private static NinaApiClient Api() => new(new HttpClient { BaseAddress = new Uri("http://localhost:1888/v2/api/"), Timeout = TimeSpan.FromMinutes(4) });

    [Fact]
    public async Task 시뮬레이터_저장된_사진의_전체_경로()
    {
        if (!Enabled) return;
        var rig = new NinaRig(Api());
        var since = DateTimeOffset.Now;
        Assert.True((await rig.CaptureAsync(1, solve: false, save: true, null, CancellationToken.None, imageType: "LIGHT")).Ok);
        var saved = await rig.LastSavedAsync(CancellationToken.None);
        output.WriteLine($"{saved}");
        Assert.NotNull(saved);
        Assert.True(saved.Value.Date >= since);
        Assert.True(Path.IsPathRooted(saved.Value.File) && File.Exists(saved.Value.File));
    }

    [Fact]
    public async Task 시뮬레이터_다크플랫은_DARKFLAT_폴더로()
    {
        if (!Enabled) return;
        var api = Api();
        var rig = new NinaRig(api);
        var wrap = new RealWrapDevices(rig, new NinaWatcher(api, NullLogger<NinaWatcher>.Instance), NullLogger<RealWrapDevices>.Instance);
        Assert.True(await wrap.CaptureAsync("DARKFLAT", 1, 800, CancellationToken.None));
        var name = Path.GetFileName((await rig.LastSavedAsync(CancellationToken.None))!.Value.File);
        var root = (await rig.ImageFolderAsync(CancellationToken.None))!;
        var found = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).ToList();
        output.WriteLine(string.Join("\n", found));
        Assert.Single(found);
        Assert.Equal("DARKFLAT", Path.GetFileName(Path.GetDirectoryName(found[0])));
    }

    /// <summary>PHD2를 시험 프로필(AA-Simulator: 카메라 Simulator · On-camera)로 가이딩 중일 때. 별을 잃은 프레임은 평균에서 빠지고 따로 세지는가, 디더링 결과</summary>
    [Fact]
    public async Task 시뮬레이터_PHD2_별_잃은_프레임_따로_세기와_디더링()
    {
        if (!Enabled) return;
        var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance);
        var dev = new Shoot.RealShootDevices(new NinaRig(Api()), phd2, new LiveImages(phd2), null!, null!, NullLogger<Shoot.RealShootDevices>.Instance);
        await dev.GuideRawAsync(CancellationToken.None); // 이벤트 받기 시작
        await Task.Delay(TimeSpan.FromSeconds(40));
        var raw = await dev.GuideRawAsync(CancellationToken.None);
        output.WriteLine($"걸음 {raw.Steps} · SNR 최소 {(raw.Snr.Count > 0 ? raw.Snr.Min() : -1):F1} · HFD 최소 {(raw.Hfd.Count > 0 ? raw.Hfd.Min() : -1):F2} · 잃음 {raw.LostRecent}/30 · HFD 낮음 {raw.LowHfdRecent}");
        Assert.True(raw.Steps > 0);
        Assert.DoesNotContain(0.0, raw.Snr); // 별을 잃은 프레임(SNR 0)은 평균에 없다
        var t = DateTime.UtcNow;
        var settled = await dev.DitherAsync(CancellationToken.None);
        output.WriteLine($"디더링 {(settled ? "안정" : "안정 못 함")} · {(DateTime.UtcNow - t).TotalSeconds:F0}초");
        dev.EndSession();
    }

    [Fact]
    public async Task 시뮬레이터_자오선_반전은_방향이_바뀐_뒤_멈출_때까지()
    {
        if (!Enabled) return;
        var rig = new NinaRig(Api());
        var m = (await rig.MountAsync(CancellationToken.None))!;
        await rig.SetTrackingAsync(true, CancellationToken.None);
        // 자오선 동쪽 바로 앞(시간각 −12초)으로 → 반전 전 자세(pierWest). N.I.N.A. 반전은 대상으로 다시 이동하는 것이라
        // 대상이 아직 동쪽이면 아무 일도 없다(2026-10-06 확인) → 자오선을 넘을 때까지 기다린 뒤 반전 (AA는 자오선 5분 뒤에 반전)
        var raH = (m.SiderealHours + 0.0033) % 24;
        Assert.Null(await rig.StartSlewAsync(raH * 15, 40, CancellationToken.None));
        Assert.NotNull(await rig.WaitSlewAsync(raH * 15, 40, null, TimeSpan.FromMinutes(3), CancellationToken.None));
        var before = (await rig.MountAsync(CancellationToken.None))!;
        output.WriteLine($"전: {before.Pier}");
        Assert.Equal("pierWest", before.Pier);
        // 적도의가 알려 주는 좌표(현재 시점)는 보낸 좌표(J2000)와 약 0.02시간 다르다 → 적도의 좌표로 자오선을 넘었는지 본다
        while (true)
        {
            var now = (await rig.MountAsync(CancellationToken.None))!;
            if (((now.SiderealHours - now.RaDeg / 15) % 24 + 24) % 24 is > 0.003 and < 12) break;
            await Task.Delay(2000);
        }

        var t = DateTime.UtcNow;
        Assert.True(await rig.FlipAsync(CancellationToken.None));
        var after = (await rig.MountAsync(CancellationToken.None))!;
        output.WriteLine($"후: {after.Pier} · {(DateTime.UtcNow - t).TotalSeconds:F0}초 · 이동 중 {after.Slewing}");
        Assert.Equal("pierEast", after.Pier);
        Assert.False(after.Slewing);
        Assert.True(DateTime.UtcNow - t > TimeSpan.FromSeconds(5)); // 움직이기 전에 끝났다고 보지 않음

        // 이미 반전된 쪽이면 명령 없이 바로 성공
        t = DateTime.UtcNow;
        Assert.True(await rig.FlipAsync(CancellationToken.None));
        Assert.True(DateTime.UtcNow - t < TimeSpan.FromSeconds(3));
    }
}
