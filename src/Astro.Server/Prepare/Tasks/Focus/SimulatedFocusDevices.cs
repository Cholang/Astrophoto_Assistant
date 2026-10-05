using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Focus;

/// <summary>④ 모의 장비. "focus.stars" = 별이 안 보여 넓게 훑어도 못 찾음, "focus.stall" = 멈춤 감지</summary>
public sealed class SimulatedFocusDevices(SimOptions sim, SimFaults faults) : IFocusDevices
{
    private int _position = 12300;
    private bool _moving, _error;

    public Task<(int Min, int Max)> LimitsAsync(CancellationToken ct) => Task.FromResult((0, 56000));
    public Task<int> PositionAsync(CancellationToken ct) => Task.FromResult(_position);
    public Task<double?> TemperatureAsync(CancellationToken ct) => Task.FromResult<double?>(12.4);

    public async Task<FocuserMove> MoveAsync(int position, CancellationToken ct)
    {
        _moving = true;
        await sim.Delay(600, ct);
        _moving = false;
        if (faults.Take("focus.stall")) { _error = true; return new FocuserMove(false, true, "포커서 멈춤 감지"); }
        _error = false;
        _position = position;
        return new FocuserMove(true);
    }

    public async Task<AutofocusRun> AutofocusAsync(Action<int, double> point, CancellationToken ct)
    {
        if (faults.IsArmed("focus.stars")) { await sim.Delay(1500, ct); return new AutofocusRun(false, false, 0, 0, false); }
        int[] pos = [11760, 11910, 12060, 12210, 12360, 12510, 12660, 12810, 12960];
        const int best = 12340;
        for (var i = 0; i < pos.Length; i++)
        {
            await sim.Delay(520, ct);
            point(pos[i], Math.Round(2.05 + Math.Pow((pos[i] - best) / 300.0, 2) * .55 + (i % 2 == 1 ? .06 : -.04), 2));
        }
        _position = best;
        return new AutofocusRun(true, true, best, 2.1, true);
    }

    public async Task<int?> CoarseSearchAsync(int min, int max, int from, Action<int> visiting, CancellationToken ct)
    {
        // 지난 위치를 중심으로 범위 안에서만 넓혀 간다
        foreach (var step in new[] { 4000, -4000, 8000, -8000 })
        {
            var p = Math.Clamp(from + step, min, max);
            visiting(p);
            await sim.Delay(700, ct);
        }
        if (faults.Take("focus.stars")) return null;
        return from;
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(50, ct);
        if (faults.Take("stop.fail")) return false;
        _moving = false;
        return true;
    }

    public Task<FocusEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new FocusEndState(_moving, _position, _error));
}
