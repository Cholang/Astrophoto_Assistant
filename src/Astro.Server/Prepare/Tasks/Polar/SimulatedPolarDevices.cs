using System.Runtime.CompilerServices;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Polar;

/// <summary>① 모의 장비: 시안 v8과 같은 흐름. 조절량은 사용자가 나사를 돌리는 것처럼 줄어든다</summary>
public sealed class SimulatedPolarDevices(SimOptions sim, SimFaults faults) : IPolarDevices
{
    private bool _sharpCapOpen, _onPhd2 = true, _rotating;
    private int _handoverFails;

    public bool OffsetUnitVerified => true; // 모의 값은 등급까지 보여 준다 (실제 모드는 실기 확인 전 false)

    public async Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct) { await sim.Delay(300, ct); return DeviceResult.Success; }

    public async Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct)
    {
        await sim.Delay(500, ct);
        if (faults.IsArmed("polar.handover"))
        {
            // 작업의 3번 시도가 모두 실패하도록, 세 번째에 꺼낸다 (다시 시도하면 성공)
            if (++_handoverFails >= 3) { faults.Take("polar.handover"); _handoverFails = 0; }
            return DeviceResult.Fail("PHD2가 가이드 카메라를 놓지 않았습니다.");
        }
        _onPhd2 = false;
        return DeviceResult.Success;
    }

    public async Task<DeviceResult> StartSharpCapAsync(CancellationToken ct)
    {
        await sim.Delay(1000, ct);
        _sharpCapOpen = true;
        return DeviceResult.Success;
    }

    public async Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct)
    {
        progress(new("first", 0, 1, 0));
        await sim.Delay(1500, ct);
        progress(new("first", 4, 2, 0)); // 별이 모자라 노출을 늘림
        await sim.Delay(1500, ct);
        if (faults.Take("polar.stars")) return new(false, "노출을 4초까지 늘려도 별이 3개뿐입니다. 가이드 망원경 덮개와 구름, 북쪽 시야를 확인해 주세요.");
        progress(new("first", 24, 2, 0));
        _rotating = true;
        for (var a = 0; a <= 60; a += 10) { progress(new("rotate", 24, 2, a)); await sim.Delay(150, ct); }
        _rotating = false;
        progress(new("second", 22, 2, 60));
        await sim.Delay(800, ct);
        return new(true);
    }

    public async IAsyncEnumerable<PolarOffset> WatchOffsetsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // 나사를 돌리는 동안 값이 줄어든다 (시안과 같은 모양)
        for (var t = 0; !ct.IsCancellationRequested; t++)
        {
            var x = t < 26 ? 140 * Math.Exp(-t / 6.0) * Math.Cos(t / 5.0) : 2;
            var y = t < 12 ? 167 : 167 * Math.Exp(-(t - 12) / 5.0) + 1;
            yield return new PolarOffset(x, y, DateTimeOffset.Now);
            if (sim.Speed <= 0 && t > 40) yield break;
            try { await sim.Delay(450, ct); } catch (OperationCanceledException) { yield break; }
        }
    }

    public async Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct)
    {
        await sim.Delay(800, ct);
        _sharpCapOpen = false;
        if (faults.Take("polar.giveback")) return DeviceResult.Fail("PHD2가 가이드 카메라를 찾지 못했습니다. USB 연결을 확인해 주세요.");
        await sim.Delay(1500, ct);
        _onPhd2 = true;
        return DeviceResult.Success;
    }

    public Task<double?> GuidePixelScaleAsync(CancellationToken ct) => Task.FromResult<double?>(4.98);

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(100, ct);
        if (faults.Take("stop.fail")) return false;
        _rotating = false;
        // 실제처럼: SharpCap을 닫고 가이드 카메라를 PHD2에 돌려준다 (RealPolarDevices.StopAsync)
        _sharpCapOpen = false;
        _onPhd2 = true;
        return true;
    }

    public Task<PolarEndState> ReadEndStateAsync(CancellationToken ct) =>
        Task.FromResult(new PolarEndState(!_sharpCapOpen, _onPhd2, false, true, _rotating));
}
