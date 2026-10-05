using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Center;

/// <summary>⑤ 모의 장비: 12.4′ → 2.1′ → 0.4′. "center.solve"가 걸리면 노출을 늘려도·하늘 전체에서도 못 찾는다</summary>
public sealed class SimulatedCenterDevices(SimOptions sim, SimFaults faults) : ICenterDevices
{
    private static readonly double[] Errors = [12.4, 2.1, 0.4];
    private int _round;
    private bool _failing;
    private double _error = 99;

    public async Task<CenterAttempt> AttemptAsync(double raDeg, double decDeg, double exposureSeconds, bool blind, CancellationToken ct)
    {
        await sim.Delay(1300, ct);
        if (!_failing && faults.Take("center.solve")) _failing = true;
        if (_failing)
        {
            if (blind) _failing = false; // 이번 판(하늘 전체까지)만 실패
            return new CenterAttempt(false, 0, null);
        }
        _error = Errors[Math.Min(_round++, Errors.Length - 1)];
        if (_error <= CenterTask.TargetArcmin) _round = 0;
        return new CenterAttempt(true, _error, 87);
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(50, ct);
        return !faults.Take("stop.fail");
    }

    public Task<CenterEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new CenterEndState(_error, false, true, false));
}
