using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Shoot;

/// <summary>
/// 촬영 모의 장비. 한 장 ≈ 4초(속도 1). 대상은 자오선 12분 전에서 시작해 사진마다 노출만큼 서쪽으로 간다(곧 반전).
/// 모의 실패: "shoot.trail"(다음 사진 별 흐름), "shoot.temp"(기온이 2.5°C 내려감 → 초점 다시), "shoot.flip"(곧바로 반전 시각), "shoot.low"(대상이 30° 아래 → 끝).
/// 가이드 별: "shoot.cloud"(서서히 약해져 잃음, 몇 번 뒤 돌아옴) · "shoot.light"(갑자기 잃음, 곧 돌아옴) · "shoot.wind"(크게 튀어 잃음) ·
/// "shoot.dew"(가이딩은 되지만 SNR이 줄고 이슬점 가까이) · "shoot.mount"(적도의 추적 멈춤) · "shoot.guider"(가이드 카메라 끊김, 다시 연결하면 됨)
/// </summary>
public sealed class SimulatedShootDevices(SimOptions sim, SimFaults faults) : IShootDevices
{
    private readonly Random _r = new(7);
    private double _ha = -3;
    private bool _trail;
    private double _temp = 12.4;
    private int _lostChecks, _dewChecks, _guideMs = 2000;
    private string? _lostKind;
    private bool _mountStopped, _disconnected;
    // 가이딩이 이미 돌고 있던 것처럼 최근 신호를 채워 둔다
    private readonly List<double> _snr = [.. Enumerable.Range(0, 10).Select(i => 30.0 + i % 3)], _hfd = [.. Enumerable.Repeat(2.2, 10)];

    public string? PhotoUrl => null;
    public int GuideExposureMs => _guideMs;

    public async Task<FrameShot> ExposeAsync(int seconds, int iso, Action<int> elapsed, CancellationToken ct)
    {
        _trail = faults.Take("shoot.trail");
        for (var i = 1; i <= 4; i++)
        {
            await sim.Delay(1000, ct);
            elapsed(seconds * i / 4);
        }
        _ha += seconds * 15.0 / 3600;
        var hfr = Math.Round(2.12 + _r.NextDouble() * .28, 2);
        var stats = _trail
            ? new FrameStats(3.4, 30, 1200, .85, 6.5)
            : new FrameStats(hfr, 44 + _r.Next(8), 1100 + _r.Next(60), .35, Math.Round(.5 + _r.NextDouble() * .2, 2));
        return new FrameShot(true, null, $"D:/Astro/sim/LIGHT_{DateTime.Now:HHmmss}.fits", stats);
    }

    public Task<double?> GuideRmsAsync(CancellationToken ct) => Task.FromResult<double?>(_trail ? 6.5 : Math.Round(.48 + _r.NextDouble() * .24, 2));

    public Task<GuideRaw> GuideRawAsync(CancellationToken ct)
    {
        if (faults.Take("shoot.mount")) _mountStopped = true;
        if (faults.Take("shoot.guider")) _disconnected = true;
        foreach (var k in new[] { "cloud", "light", "wind" })
            if (faults.Take("shoot." + k)) { _lostKind = k; _lostChecks = k == "cloud" ? 4 : 2; }
        if (faults.Take("shoot.dew")) _dewChecks = 40;

        double? jump = null;
        var guiding = !_mountStopped && !_disconnected;
        double snr = 30 + _r.NextDouble() * 6;
        if (_lostChecks > 0)
        {
            _lostChecks--;
            guiding = false;
            // 구름은 잃기 전에 신호가 줄고, 빛·바람은 직전까지 그대로
            if (_lostKind == "cloud") { snr = 4; for (var i = 0; i < 3; i++) Push(_snr, 8 - i * 2); }
            if (_lostKind == "wind") jump = 9;
        }
        else if (_dewChecks > 0) { _dewChecks--; snr = 9 + _r.NextDouble() * 2; }
        Push(_snr, snr);
        Push(_hfd, 2.2 + _r.NextDouble() * .2);
        return Task.FromResult(new GuideRaw(!_disconnected, guiding, !_mountStopped, _snr.ToArray(), _hfd.ToArray(), jump, _dewChecks > 0 ? 1.2 : 6));
    }

    private static void Push(List<double> xs, double v)
    {
        xs.Add(v);
        if (xs.Count > 60) xs.RemoveAt(0);
    }

    public Task<bool> SetGuideExposureAsync(int ms, CancellationToken ct) { _guideMs = ms; return Task.FromResult(true); }
    public async Task<bool> ReselectStarAsync(CancellationToken ct) { await sim.Delay(500, ct); _lostChecks = 0; return true; }
    public async Task<bool> ReconnectGuiderAsync(CancellationToken ct) { await sim.Delay(800, ct); _disconnected = false; return true; }
    public async Task<bool> RecenterAsync(PrepContext ctx, CancellationToken ct) { await sim.Delay(1500, ct); return true; }
    public async Task<bool> AbortExposureAsync(CancellationToken ct) { await sim.Delay(100, ct); return true; }
    public Task<bool> DewHeaterBoostAsync(CancellationToken ct) => Task.FromResult(false);
    public async Task<bool> ResumeGuidingAsync(CancellationToken ct) { await sim.Delay(600, ct); return !_mountStopped && !_disconnected; }
    public async Task<bool> StopGuidingAsync(CancellationToken ct) { await sim.Delay(200, ct); return !faults.Take("stop.fail"); }
    public async Task<bool> DitherAsync(CancellationToken ct) { await sim.Delay(1200, ct); return true; }

    public async Task<bool> FlipAsync(PrepContext ctx, Action<int> step, CancellationToken ct)
    {
        for (var i = 0; i < 3; i++) { step(i); await sim.Delay(1500, ct); }
        _ha = -Math.Abs(_ha);
        return true;
    }

    public async Task<RefocusOutcome> RefocusAsync(Action<int, double> point, CancellationToken ct)
    {
        int[] pos = [11760, 11910, 12060, 12210, 12360, 12510, 12660, 12810, 12960];
        const int best = 12410;
        foreach (var p in pos)
        {
            await sim.Delay(350, ct);
            point(p, Math.Round(2.05 + Math.Pow((p - best) / 300.0, 2) * .55, 2));
        }
        return new RefocusOutcome(true, best, 2.1);
    }

    public Task<double?> TemperatureAsync(CancellationToken ct)
    {
        if (faults.Take("shoot.temp")) _temp -= 2.5;
        return Task.FromResult<double?>(_temp);
    }

    public string? MoveToExcluded(string file) => Path.Combine(Path.GetDirectoryName(file) ?? "", "제외", Path.GetFileName(file));

    public double HourAngleDeg(PrepContext ctx)
    {
        if (faults.Take("shoot.flip")) _ha = 1.3;
        return _ha;
    }

    public double TargetAltitude(PrepContext ctx) => faults.IsArmed("shoot.low") ? 28 : 55;
}
