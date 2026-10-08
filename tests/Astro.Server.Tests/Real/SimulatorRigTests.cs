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
        await Task.Delay(1100); // 앞 시험과 같은 초에 찍으면 이름이 같은 파일이 다른 폴더에 생겨 일부러 못 찾음(후보 둘 → null)
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
        var dev = new Shoot.RealShootDevices(new NinaRig(Api()), phd2, new LiveImages(phd2), null!, null!, new AscomWeather(NullLogger<AscomWeather>.Instance), NullLogger<Shoot.RealShootDevices>.Instance);
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

    /// <summary>
    /// 촬영 세션 전체를 실장비 코드(RealShootDevices)로: 찍기·저장·등급·제외 폴더·디더링·가이딩 감시·이슬·끝(가이딩 정지).
    /// 시뮬레이터 카메라가 잡음 사진이면 별이 없어 등급은 의미 없음 — 흐름과 명령이 끝까지 가는지를 본다 (2026-10-08)
    /// </summary>
    [Fact]
    public async Task 시뮬레이터_촬영_세션_실장비_코드로_끝까지()
    {
        if (!Enabled) return;
        var api = Api();
        var rig = new NinaRig(api);
        var phd2 = new Phd2Client(NullLogger<Phd2Client>.Instance);
        var live = new LiveImages(phd2);
        // 대상: 자오선 동쪽 1시간 (반전 없음, 높이 충분)
        var m = (await rig.MountAsync(CancellationToken.None))!;
        await rig.SetTrackingAsync(true, CancellationToken.None);
        var ra = ((m.SiderealHours + 1) % 24) * 15;
        Assert.Null(await rig.StartSlewAsync(ra, 45, CancellationToken.None));
        Assert.NotNull(await rig.WaitSlewAsync(ra, 45, null, TimeSpan.FromMinutes(3), CancellationToken.None));
        // 가이딩 (대상 단계가 해 둔 상태)
        using (var ev = await phd2.SubscribeAsync(CancellationToken.None))
        {
            await phd2.CallAsync("guide", new { settle = new { pixels = 1.5, time = 5, timeout = 120 }, recalibrate = true }, CancellationToken.None);
            var settled = await ev.WaitAsync(["SettleDone"], TimeSpan.FromMinutes(4), CancellationToken.None);
            output.WriteLine($"가이딩 시작: {settled}");
        }
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dev = new Shoot.RealShootDevices(rig, phd2, live, new RealCenterDevices(rig, live), new RealFocusDevices(rig, config, new()),
            new AscomWeather(NullLogger<AscomWeather>.Instance), NullLogger<Shoot.RealShootDevices>.Instance);
        var session = new Shoot.ShootSession(dev, new global::Astro.Server.Prepare.PrepareMode(false), NullLogger<Shoot.ShootSession>.Instance);
        var results = new global::Astro.Server.Prepare.Flow.PrepResults();
        // 초점 때 기온 = 지금 포커서 온도 (다르면 바로 초점 다시 맞추기로 감)
        results.Set(new global::Astro.Server.Prepare.Flow.FocusResult(false, 25000, 2.0, (await rig.FocuserAsync(CancellationToken.None))?.Temperature, DateTimeOffset.Now));
        var plan = Prepare.Harness.Plan() with { RaDegrees = ra, DecDegrees = 45, ExposureSeconds = 3, EstimatedFrames = 4 };
        var ctx = Prepare.Harness.TargetContext(results, new global::Astro.Server.Prepare.Flow.InMemoryPrepMemory(), plan);
        session.Start(ctx);
        var notes = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(8);
        Shoot.ShootView v;
        while ((v = session.View()).Mode != Shoot.ShootMode.Ended && DateTime.UtcNow < deadline)
        {
            if (v.Note is { } n && (notes.Count == 0 || notes[^1] != n.Text)) notes.Add(n.Text);
            await session.NextChangeAsync().WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
        }
        output.WriteLine($"끝: {v.Ended} · 쓸 {v.Good} · 제외 {v.Excluded} · 등급 {string.Join(",", v.Tally.Where(t => t.Value > 0).Select(t => $"{t.Key}{t.Value}"))} · 가이딩 정지 {v.GuideStopped} · 이슬 {v.Dew}");
        output.WriteLine("상태: " + string.Join(" → ", session.ModeHistory));
        foreach (var n in notes) output.WriteLine("알림: " + n);
        if (results.Get<Shoot.NightShootResult>()?.Targets.SingleOrDefault() is { } shots)
            output.WriteLine($"폴더 {shots.Folder} · 못 옮김 {shots.NotMoved?.Count ?? 0}");
        Assert.Equal(Shoot.ShootMode.Ended, v.Mode);
        Assert.True(v.GuideStopped);
    }

    /// <summary>마무리 실장비 코드: 플랫용 위쪽 이동 · 노출 찾기 · 홈 · 추적 끄기 (프로그램 닫기는 하지 않음, 2026-10-08)</summary>
    [Fact]
    public async Task 시뮬레이터_마무리_위쪽_노출찾기_홈_추적끄기()
    {
        if (!Enabled) return;
        var api = Api();
        var rig = new NinaRig(api);
        var wrap = new RealWrapDevices(rig, new NinaWatcher(api, NullLogger<NinaWatcher>.Instance), NullLogger<RealWrapDevices>.Instance);
        await rig.SetTrackingAsync(true, CancellationToken.None);
        var up = await wrap.PointUpAsync(CancellationToken.None);
        var m = (await rig.MountAsync(CancellationToken.None))!;
        output.WriteLine($"위쪽: {up} · Dec {m.DecDeg:F1} (위도 {m.Site.Latitude:F1})");
        var tries = new List<string>();
        var flat = await wrap.FindFlatExposureAsync(800, (s, pct) => tries.Add($"{s:0.###}초→{pct:F0}%"), CancellationToken.None);
        output.WriteLine($"플랫 노출: {flat} · 시도 {string.Join(", ", tries)}");
        var home = await wrap.HomeAsync(CancellationToken.None);
        var off = await wrap.TrackingOffAsync(CancellationToken.None);
        m = (await rig.MountAsync(CancellationToken.None))!;
        output.WriteLine($"홈 {home} (AtHome {m.AtHome}) · 추적 끔 {off} (Tracking {m.Tracking})");
        Assert.True(up);
        Assert.True(off && !m.Tracking);
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
        Assert.True((await rig.FlipAsync(CancellationToken.None)).Flipped);
        var after = (await rig.MountAsync(CancellationToken.None))!;
        output.WriteLine($"후: {after.Pier} · {(DateTime.UtcNow - t).TotalSeconds:F0}초 · 이동 중 {after.Slewing}");
        Assert.Equal("pierEast", after.Pier);
        Assert.False(after.Slewing);
        Assert.True(DateTime.UtcNow - t > TimeSpan.FromSeconds(5)); // 움직이기 전에 끝났다고 보지 않음

        // 이미 반전된 쪽이면 명령 없이 멈춤만 확인하고 성공 (CX-NIGHT-01)
        t = DateTime.UtcNow;
        Assert.True((await rig.FlipAsync(CancellationToken.None)).Flipped);
        Assert.True(DateTime.UtcNow - t < TimeSpan.FromSeconds(6));
    }
}
