using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Guiding;

/// <summary>⑥ 모의 장비. "guiding.star"가 걸리면 노출을 늘려도 별을 못 찾는다, "guiding.calibration"이면 한 번 보정값 불일치</summary>
public sealed class SimulatedGuidingDevices(SimOptions sim, SimFaults faults) : IGuidingDevices
{
    private bool _guiding;

    public async Task<GuideStart> StartAsync(double exposureSeconds, Action<string> phase, CancellationToken ct)
    {
        await sim.Delay(1000, ct);
        if (faults.Take("guiding.calibration")) return new GuideStart(false, true, CalibrationMismatch: true, Problem: "보정값 불일치");
        if (faults.IsArmed("guiding.star"))
        {
            if (exposureSeconds >= 3) faults.Take("guiding.star"); // 늘린 노출까지 실패 → 다시 시도하면 성공
            return new GuideStart(false, false, Problem: "별 없음");
        }
        phase("settle");
        await sim.Delay(2000, ct);
        _guiding = true;
        return new GuideStart(true, true);
    }

    public async Task<GuideStats> MeasureAsync(Action<double, double> sample, CancellationToken ct)
    {
        var r = new Random(7);
        double sra = 0, sde = 0;
        const int n = 30;
        for (var i = 0; i < n; i++)
        {
            var ra = (r.NextDouble() - .5) * 1.5;
            var de = (r.NextDouble() - .5) * 1.2;
            sra += ra * ra; sde += de * de;
            sample(Math.Abs(ra), Math.Abs(de));
            await sim.Delay(160, ct);
        }
        return new GuideStats(Math.Sqrt(sra / n), Math.Sqrt(sde / n), 0);
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(50, ct);
        if (faults.Take("stop.fail")) return false;
        _guiding = false;
        return true;
    }

    public Task<GuidingEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new GuidingEndState(_guiding, _guiding, 0));
}
