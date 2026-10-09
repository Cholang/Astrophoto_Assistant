using Astro.Nina;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Astro.Server.Tests.Real;

/// <summary>
/// 실장비 시험 (P3). 장비가 실제로 움직인다 — 평소에는 아무것도 하지 않고 통과한다.
/// 사용자 허락을 받고 환경 변수 AA_REAL=1과 함께 하나씩 돌린다 (예: dotnet test --filter 실장비_시험_사진).
/// 실내에서 볼 수 있는 것만: 별이 필요한 결과(솔빙·캘리브레이션·가이딩·자동초점)는 "못 찾음" 경로까지
/// </summary>
public class RealDeviceTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AA_REAL") == "1";

    private static NinaRig Rig() => new(new NinaApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:1888/v2/api/"), Timeout = TimeSpan.FromMinutes(4) }));
    private static Phd2Client Phd2() => new(NullLogger<Phd2Client>.Instance);
    private static string Out(string name) => Path.Combine(Path.GetTempPath(), "aa-real-" + name);

    [Fact]
    public async Task 실장비_시험_사진()
    {
        if (!Enabled) return;
        var live = new LiveImages(Phd2(), new NinaApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:1888/v2/api/") }));
        var dev = new RealTestShotDevices(Rig(), live);
        var since = DateTimeOffset.Now;
        Assert.True(await dev.ExposeAsync(1, 800, s => output.WriteLine($"남은 {s}초"), CancellationToken.None));
        var stats = await dev.DownloadAndAnalyzeAsync(since, CancellationToken.None);
        output.WriteLine($"stats: {stats}");
        Assert.NotNull(stats);
        var end = await dev.ReadEndStateAsync(CancellationToken.None);
        output.WriteLine($"end: {end}");
        if (await live.GetAsync("test", CancellationToken.None) is { } img) await File.WriteAllBytesAsync(Out("test.jpg"), img.Bytes);
    }

    /// <summary>WandererBox 기온·습도·이슬점 (읽기만, 2026-10-08)</summary>
    [Fact]
    public async Task 실장비_기온_습도_이슬점()
    {
        if (!Enabled) return;
        var w = new AscomWeather(NullLogger<AscomWeather>.Instance);
        // 기다리지 않는 조회 (CX-NIGHT-04): 처음엔 null, 뒤에서 읽힌 뒤 값
        AscomWeather.Reading? r = null;
        for (var i = 0; i < 40 && r is null; i++) { r = await w.ReadAsync(CancellationToken.None); if (r is null) await Task.Delay(250); }
        output.WriteLine($"{r} · 여유 {r?.MarginC:F1}°C");
        Assert.NotNull(r);
        Assert.InRange(r.HumidityPercent, 1, 100);
        Assert.True(r.DewPointC < r.TemperatureC);
        Assert.Same(r, await w.ReadAsync(CancellationToken.None)); // 1분 안에는 다시 읽지 않음
    }

    /// <summary>WandererBox 열선(DC3 PWM) 세기·자동 여부 — N.I.N.A. 스위치를 연결해 둔 상태에서 (읽기만, 2026-10-08)</summary>
    [Fact]
    public async Task 실장비_열선_상태()
    {
        if (!Enabled) return;
        var h = await Rig().DewHeaterAsync(CancellationToken.None);
        output.WriteLine($"{h}");
        Assert.NotNull(h);
        Assert.InRange(h.Value.Power, 0, 1);
    }

    [Fact]
    public async Task 실장비_포커서_300걸음_갔다_돌아오기()
    {
        if (!Enabled) return;
        var config = new ConfigurationBuilder().Build();
        var dev = new RealFocusDevices(Rig(), config, new(), new global::Astro.Server.Engine.OpticsStore(Path.GetTempPath()));
        var start = await dev.PositionAsync(CancellationToken.None);
        output.WriteLine($"시작 {start}, 기온 {await dev.TemperatureAsync(CancellationToken.None)}");
        var go = await dev.MoveAsync(start + 300, CancellationToken.None);
        output.WriteLine($"+300: {go} → {await dev.PositionAsync(CancellationToken.None)}");
        var back = await dev.MoveAsync(start, CancellationToken.None);
        output.WriteLine($"돌아옴: {back} → {await dev.PositionAsync(CancellationToken.None)}");
        Assert.True(go.Ok && back.Ok);
        Assert.Equal(start, await dev.PositionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 실장비_대상_이동과_멈춤()
    {
        if (!Enabled) return;
        var rig = Rig();
        var dev = new RealSlewDevices(rig);
        var m = await rig.MountAsync(CancellationToken.None);
        Assert.NotNull(m);
        // 자오선 근처 적위 60° (홈에서 가까운 곳)
        var ra = m!.SiderealHours * 15 % 360;
        var moved = await dev.SlewToTargetAsync(ra, 60, d => output.WriteLine($"남은 {d:F1}°"), CancellationToken.None);
        output.WriteLine($"도착: {moved}");
        Assert.True(moved.Ok);
        // 다시 적위 75°로 가다가 멈춤
        Assert.Null(await rig.StartSlewAsync(ra, 75, CancellationToken.None));
        await Task.Delay(1500);
        var stopped = await dev.StopAsync(CancellationToken.None);
        output.WriteLine($"멈춤 확인: {stopped}, 지금 {await rig.MountAsync(CancellationToken.None)}");
        Assert.True(stopped);
    }

    [Fact]
    public async Task 실장비_캘리브레이션_위치A와_별_선택()
    {
        if (!Enabled) return;
        await using var phd2 = Phd2();
        var dev = new RealCalibrationDevices(Rig(), phd2);
        var at = await dev.SlewToHourAngleAsync(+1, 0, CancellationToken.None);
        output.WriteLine($"위치 A: {at}");
        var star = await dev.SelectStarAsync(CancellationToken.None); // 실내: 별 없음
        output.WriteLine($"별: {star}, PHD2 {await phd2.AppStateAsync(CancellationToken.None)}");
        output.WriteLine($"보정값 있음: {await dev.HasCalibrationAsync(CancellationToken.None)}");
        Assert.True(at.Ok);
    }

    [Fact]
    public async Task 실장비_가이딩_시작_별_없음()
    {
        if (!Enabled) return;
        await using var phd2 = Phd2();
        var dev = new RealGuidingDevices(Rig(), phd2);
        var start = await dev.StartAsync(1.5, p => output.WriteLine($"단계 {p}"), CancellationToken.None);
        output.WriteLine($"시작: {start}, PHD2 {await phd2.AppStateAsync(CancellationToken.None)}");
        Assert.False(start.StarFound);
        Assert.True(await dev.StopAsync(CancellationToken.None));
        Assert.Equal("Stopped", await phd2.AppStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 실장비_센터링_한_회차_솔빙_실패()
    {
        if (!Enabled) return;
        var live = new LiveImages(Phd2(), new NinaApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:1888/v2/api/") }));
        var dev = new RealCenterDevices(Rig(), live);
        var m = await Rig().MountAsync(CancellationToken.None);
        var a = await dev.AttemptAsync(m!.RaDeg, m.DecDeg, 2, false, CancellationToken.None);
        output.WriteLine($"회차: {a}");
        Assert.False(a.Solved); // 실내: 별이 없어 솔빙 실패
    }
}
