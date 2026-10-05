using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Slew;

/// <summary>③ 모의 장비. "slew.low"가 걸리면 대상이 14°에서 시작해 기다리는 동안 올라온다 (시안: 1틱 = 1분)</summary>
public sealed class SimulatedSlewDevices(SimOptions sim, SimFaults faults) : ISlewDevices
{
    private double? _lowAltitude;
    private bool _moving, _tracking = true;
    private double _distance;

    public double TargetAltitude(PrepContext ctx)
    {
        if (_lowAltitude is null && faults.Take("slew.low")) _lowAltitude = 14;
        return _lowAltitude ?? 42;
    }

    public int? MinutesUntilUsable(PrepContext ctx) => _lowAltitude is { } a && a < SlewTask.UsableAltitude ? (int)Math.Ceiling((SlewTask.UsableAltitude - a) / 17.0 * 18) : 0;

    public async Task WaitTickAsync(CancellationToken ct)
    {
        await sim.Delay(1000, ct);
        if (_lowAltitude is { } a) _lowAltitude = a >= 31 ? null : Math.Min(31, a + 17.0 / 18);
    }

    public async Task<SlewMove> SlewToTargetAsync(double raDeg, double decDeg, Action<double> remainingDeg, CancellationToken ct)
    {
        _moving = true;
        try
        {
            var fail = faults.Take("slew.move");
            for (var k = 0; k <= 30; k++)
            {
                _distance = Math.Max(0.4, 91 * Math.Pow(1 - k / 30.0, 2.2));
                remainingDeg(_distance);
                if (fail && k == 18) return new SlewMove(false, _distance, "적도의가 응답하지 않습니다. 다시 연결해 봤지만 안 됩니다.");
                await sim.Delay(100, ct);
            }
            _tracking = true;
            return new SlewMove(true, 0.4);
        }
        finally { _moving = false; }
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(100, ct);
        if (faults.Take("stop.fail")) return false;
        _moving = false;
        return true;
    }

    public Task<SlewEndState> ReadEndStateAsync(PrepContext ctx, CancellationToken ct) =>
        Task.FromResult(new SlewEndState(_distance, _moving, _tracking));
}
