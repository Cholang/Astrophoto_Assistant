using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Calibration;

/// <summary>② 모의 장비. "calibration.star"가 걸리면 위치 A에서 별을 못 찾아 B로 간다</summary>
public sealed class SimulatedCalibrationDevices(SimOptions sim, SimFaults faults) : ICalibrationDevices
{
    private bool _hasCalibration, _moving;

    public async Task<SlewOutcome> SlewToHourAngleAsync(double hourAngle, double decDeg, CancellationToken ct)
    {
        _moving = true;
        await sim.Delay(2400, ct);
        _moving = false;
        return new SlewOutcome(true, 0.38);
    }

    public async Task<StarPick> SelectStarAsync(CancellationToken ct)
    {
        await sim.Delay(2000, ct);
        if (faults.Take("calibration.star")) return new StarPick(false, 0, 3);
        return new StarPick(true, 42, 2);
    }

    public async Task<CalibrationData> CalibrateAsync(Action<string, int> step, CancellationToken ct)
    {
        for (var n = 1; n <= 12; n++) { step("west", n); await sim.Delay(140, ct); }
        for (var n = 1; n <= 12; n++) { step("north", n); await sim.Delay(140, ct); }
        if (faults.Take("calibration.measure")) return new CalibrationData(false, 0, 0, 0, [], "적위 쪽 움직임이 너무 작습니다");
        _hasCalibration = true;
        return new CalibrationData(true, 1.2, 1.0, 0.96, []);
    }

    public Task<bool> HasCalibrationAsync(CancellationToken ct) => Task.FromResult(_hasCalibration);

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(100, ct);
        if (faults.Take("stop.fail")) return false;
        _moving = false;
        return true;
    }

    public Task<CalibrationEndState> ReadEndStateAsync(CancellationToken ct) =>
        Task.FromResult(new CalibrationEndState(_hasCalibration, false, false, _moving, true));
}
