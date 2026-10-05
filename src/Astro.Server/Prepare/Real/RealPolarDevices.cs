using System.Runtime.CompilerServices;
using System.Text.Json;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Tasks.Polar;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ① 극축 정렬 실장비 (2026-10-04~06 실기로 확인한 순서): 추적 항성(N.I.N.A.) → PHD2가 가이드 카메라를 놓음(stop_capture → Stopped → set_connected false)
/// → SharpCap 실행(스크립트가 극축 정렬을 켜고 상태 보고) → 첫 사진 위치 찾기 → 다음 단계 → RA축 60° 회전(ASCOM MoveAxis) → 둘째 사진 → 조절
/// → 돌려주기(SharpCap 닫기 → PHD2 set_connected true → N.I.N.A.–PHD2 확인).
/// 별이 필요한 부분(위치 찾기·조절 단계 이름·조절량 단위)과 회전 방향은 맑은 날 확인.
/// </summary>
public sealed class RealPolarDevices(NinaRig rig, Phd2Client phd2, SharpCapBridge sharpCap, AscomAxis axis, ReportAddress address) : IPolarDevices
{
    /// <summary>RA 회전: 이 적도의는 홈에서 한쪽으로 약 60°까지만 돈다 (DESIGN.md ①)</summary>
    private const double RotationDeg = 60;
    private const double RotationRate = 2.0; // °/s (최대 3.55)
    private static readonly double[] Exposures = [1, 2, 4]; // 초 — 별이 모자라면 늘림 (SharpCap 권장 1~2초에서 시작)

    private string _camera = "";

    public bool OffsetUnitVerified => false; // 조절량 단위·환산은 맑은 날 실기 확인 전 (CX-PREP-IMPL-03)

    public async Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct) =>
        await rig.SetTrackingAsync(true, ct) ? DeviceResult.Success : DeviceResult.Fail("추적 속도를 항성으로 맞추지 못했습니다");

    public async Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct)
    {
        try
        {
            if (await CameraAsync(ct) is { } cam && cam.Name.Length > 0) _camera = cam.Name; // SharpCap에 같은 카메라를 연다
            await phd2.CallAsync("stop_capture", ct: ct);
            for (var i = 0; i < 50 && await phd2.AppStateAsync(ct) != "Stopped"; i++) await Task.Delay(200, ct);
            await phd2.CallAsync("set_connected", new object[] { false }, ct);
            return await CameraAsync(ct) is { Connected: false }
                ? DeviceResult.Success
                : DeviceResult.Fail("PHD2가 가이드 카메라를 놓지 않았습니다.");
        }
        catch (Phd2Exception e)
        {
            return DeviceResult.Fail(e.Message);
        }
    }

    public async Task<DeviceResult> StartSharpCapAsync(CancellationToken ct)
    {
        if (_camera.Length == 0) return DeviceResult.Fail("가이드 카메라 이름을 알지 못합니다. PHD2에서 가이드 카메라를 연결해 주세요.");
        var problem = await sharpCap.LaunchAsync(_camera, address.SharpCapReportUrl, ct);
        return problem is null ? DeviceResult.Success : DeviceResult.Fail(problem);
    }

    public async Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct)
    {
        // 첫 사진: 위치를 찾을 때까지 (노출을 늘려 가며)
        if (!await SolveStageAsync("first", progress, 0, ct))
            return new(false, "노출을 4초까지 늘려도 위치를 찾지 못했습니다. 가이드 망원경 덮개와 구름, 북쪽 시야를 확인해 주세요.");
        sharpCap.Send("advance");
        await sharpCap.WaitAsync(r => r.Stage != "First", TimeSpan.FromSeconds(15), ct);

        // RA축만 돌린다 (SharpCap은 안내만)
        var rotated = await axis.RotateRaAsync(RotationRate, RotationDeg / RotationRate,
            deg => progress(new("rotate", 0, 0, deg)), ct);
        if (rotated is not null) return new(false, rotated);
        if (!await rig.ConfirmStillAsync(ct)) return new(false, "적도의가 멈췄는지 확인하지 못했습니다. 적도의 상태를 확인해 주세요.");

        // 둘째 사진
        if (!await SolveStageAsync("second", progress, RotationDeg, ct))
            return new(false, "회전한 뒤 위치를 찾지 못했습니다. 회전 중 별이 가려졌거나 회전이 부족했을 수 있습니다.");
        sharpCap.Send("advance");
        return await sharpCap.WaitAsync(r => r.Adjusting, TimeSpan.FromSeconds(20), ct) is null
            ? new(false, "SharpCap이 조절 단계로 넘어가지 않았습니다")
            : new(true);
    }

    /// <summary>지금 단계에서 SharpCap이 위치를 찾을(CanAdvance) 때까지. 못 찾으면 노출을 늘려 다시</summary>
    private async Task<bool> SolveStageAsync(string stage, Action<PoleProgress> progress, double rotation, CancellationToken ct)
    {
        foreach (var exp in Exposures)
        {
            sharpCap.Send($"exposure:{exp * 1000}");
            progress(new(stage, 0, exp, rotation));
            // 노출을 바꾼 뒤 사진 몇 장이 지나가도록
            if (await sharpCap.WaitAsync(r => r.CanAdvance, TimeSpan.FromSeconds(10 + exp * 6), ct) is not null) return true;
            if (!sharpCap.Running) return false;
        }
        return false;
    }

    public async IAsyncEnumerable<PolarOffset> WatchOffsetsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var last = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            if (sharpCap.Last is { Adjusting: true } r && r.At > last)
            {
                last = r.At;
                yield return new PolarOffset(r.X, r.Y, r.At);
            }
            try { await Task.Delay(300, ct); } catch (OperationCanceledException) { yield break; }
        }
    }

    public async Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct)
    {
        if (!await sharpCap.CloseAsync(ct)) return DeviceResult.Fail("SharpCap을 닫지 못했습니다. SharpCap을 직접 닫아 주세요.");
        try
        {
            await phd2.CallAsync("set_connected", new object[] { true }, ct, TimeSpan.FromSeconds(60));
            if (await CameraAsync(ct) is not { Connected: true }) return DeviceResult.Fail("PHD2가 가이드 카메라를 찾지 못했습니다. USB 연결을 확인해 주세요.");
        }
        catch (Phd2Exception e)
        {
            return DeviceResult.Fail(e.Message);
        }
        return await rig.GuiderConnectedAsync(ct) ? DeviceResult.Success : DeviceResult.Fail("N.I.N.A.가 PHD2에 연결되어 있지 않습니다");
    }

    public async Task<double?> GuidePixelScaleAsync(CancellationToken ct)
    {
        try { return (await phd2.CallAsync("get_pixel_scale", ct: ct)).GetDouble(); }
        catch (Exception e) when (e is Phd2Exception or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// 멈춤: RA 회전·이동을 멈추고 확인. 중간에 멈춰도 장비를 원래대로 — SharpCap이 열려 있으면 닫고 가이드 카메라를 PHD2에 돌려준다
    /// (그러지 않으면 PHD2가 카메라 없이 남아 다음 작업이 가이딩을 못 함)
    /// </summary>
    public async Task<bool> StopAsync(CancellationToken ct)
    {
        axis.Stop();
        var still = await rig.StopSlewAndConfirmAsync(ct);
        if (sharpCap.Running || await CameraAsync(ct) is { Connected: false })
        {
            await sharpCap.CloseAsync(ct);
            try { await phd2.CallAsync("set_connected", new object[] { true }, ct, TimeSpan.FromSeconds(60)); }
            catch (Phd2Exception) { /* 장비 상태 확인 필요로 남는다 — 아래 still과 별개로 사용자에게 보임 */ }
        }
        return still;
    }

    public async Task<PolarEndState> ReadEndStateAsync(CancellationToken ct)
    {
        var cam = await CameraAsync(ct);
        string state;
        try { state = await phd2.AppStateAsync(ct); } catch (Phd2Exception) { state = ""; }
        var mount = await rig.MountAsync(ct);
        return new PolarEndState(!sharpCap.Running, cam is { Connected: true }, state == "Looping",
            await rig.GuiderConnectedAsync(ct), mount is null || mount.Slewing);
    }

    private async Task<(string Name, bool Connected)?> CameraAsync(CancellationToken ct)
    {
        try
        {
            var eq = await phd2.CallAsync("get_current_equipment", ct: ct);
            return eq.TryGetProperty("camera", out var c) && c.ValueKind == JsonValueKind.Object
                ? (c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", c.TryGetProperty("connected", out var k) && k.GetBoolean())
                : null;
        }
        catch (Phd2Exception) { return null; }
    }
}

/// <summary>SharpCap 스크립트가 상태를 보낼 AA 주소 (서버가 실제로 듣는 주소)</summary>
public sealed class ReportAddress(Microsoft.AspNetCore.Hosting.Server.IServer server)
{
    public string SharpCapReportUrl
    {
        get
        {
            var addr = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                ?? "http://localhost:5210";
            return addr.TrimEnd('/').Replace("localhost", "127.0.0.1") + "/api/prepare/sharpcap/report";
        }
    }
}
