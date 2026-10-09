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
    // 노출(초): 1초에서 시작해 배경 밝기를 보고 0.25~4초 사이에서 줄이거나 늘린다 (2026-10-08 실기: 무조건 늘리면 화면이 하얘져 별이 더 안 보임)
    private const double MinExposure = 0.25, MaxExposure = 4;
    private const double Bright = 0.4, Dark = 0.02; // 배경 밝기(0~1, 최대 신호 대비): 이보다 밝으면 줄이고, 어두우면 늘린다
    private double _exposure = 1;

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
            return DeviceResult.Fail(Phd2Words(e.Message));
        }
    }

    public Task<bool> Phd2DialogOpenAsync(CancellationToken ct) => Task.FromResult(Phd2Windows.ConnectDialogOpen());

    /// <summary>PHD2가 영어로 주는 이유를 사용자가 할 일로 (2026-10-08 실기: 장비 연결 창이 열려 있으면 카메라를 놓지 못함)</summary>
    internal static string Phd2Words(string message) =>
        message.Contains("Connect Equipment dialog", StringComparison.OrdinalIgnoreCase)
            ? "PHD2의 장비 연결 창이 열려 있어 가이드 카메라를 놓지 못했습니다. 그 창을 닫은 뒤 다시 시도해 주세요."
            : message;

    public async Task<DeviceResult> StartSharpCapAsync(CancellationToken ct)
    {
        if (_camera.Length == 0) return DeviceResult.Fail("가이드 카메라 이름을 알지 못합니다. PHD2에서 가이드 카메라를 연결해 주세요.");
        if (_camera.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
            return DeviceResult.Fail("PHD2의 가이드 카메라가 시뮬레이터(Simulator)로 되어 있어 SharpCap이 열 수 없습니다. PHD2 장비 연결 창에서 실제 가이드 카메라를 고른 뒤 다시 시도해 주세요.");
        var problem = await sharpCap.LaunchAsync(_camera, address.SharpCapReportUrl, ct);
        return problem is null ? DeviceResult.Success : DeviceResult.Fail(problem);
    }

    public async Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct)
    {
        // 첫 사진: 위치를 찾을 때까지 (노출을 늘려 가며)
        _exposure = 1;
        if (await SolveStageAsync("first", progress, 0, ct) is { } why1) return new(false, why1);
        sharpCap.Send("advance");
        await sharpCap.WaitAsync(r => r.Stage != "First", TimeSpan.FromSeconds(15), ct);

        // RA축만 돌린다 (SharpCap은 안내만)
        var rotated = await axis.RotateRaAsync(RotationRate, RotationDeg / RotationRate,
            deg => progress(new("rotate", 0, 0, deg)), ct);
        if (rotated is not null) return new(false, rotated);
        if (!await rig.ConfirmStillAsync(ct)) return new(false, "적도의가 멈췄는지 확인하지 못했습니다. 적도의 상태를 확인해 주세요.");

        // 둘째 사진
        if (await SolveStageAsync("second", progress, RotationDeg, ct) is { } why2)
            return new(false, $"회전한 뒤 위치를 찾지 못했습니다. 회전 중 별이 가려졌을 수 있습니다. ({why2})");
        sharpCap.Send("advance");
        return await sharpCap.WaitAsync(r => r.Adjusting, TimeSpan.FromSeconds(20), ct) is null
            ? new(false, "SharpCap이 조절 단계로 넘어가지 않았습니다")
            : new(true);
    }

    /// <summary>
    /// 지금 단계에서 SharpCap이 위치를 찾을(CanAdvance) 때까지. 찾으면 null, 못 찾으면 이유.
    /// 못 찾으면 배경 밝기를 보고: 밝으면(하얗게 뜸) 노출을 줄이고, 어두우면 늘리고, 적당한데도 못 찾으면 노출 탓이 아니므로 멈추고 알린다.
    /// 사용자가 SharpCap에서 노출을 직접 바꾸면 그 뒤로는 AA가 바꾸지 않고 그 값으로 기다린다 (2026-10-08 사용자 요청)
    /// </summary>
    private async Task<string?> SolveStageAsync(string stage, Action<PoleProgress> progress, double rotation, CancellationToken ct)
    {
        if (!sharpCap.UserChangedExposure) sharpCap.Send($"exposure:{_exposure * 1000}");
        for (var round = 0; round < 8; round++)
        {
            var user = sharpCap.UserChangedExposure;
            var exp = user && sharpCap.Last is { ExposureMs: > 0 } l ? l.ExposureMs / 1000 : _exposure;
            progress(new(stage, 0, exp, rotation, user));
            // 노출을 바꾼 뒤 사진 몇 장이 지나가도록. 직접 바꾼 값이면 더 길게 기다린다 (그사이 또 바꿀 수 있음)
            if (await sharpCap.WaitAsync(r => r.CanAdvance, TimeSpan.FromSeconds((user ? 30 : 10) + exp * 6), ct) is not null) return null;
            if (!sharpCap.Running) return "SharpCap이 닫혔습니다";
            if (sharpCap.UserChangedExposure)
            {
                if (user) return $"SharpCap에서 직접 정한 노출({exp:0.##}초)로도 위치를 찾지 못했습니다. 망원경이 북극에서 멀리 향해 있을 수 있습니다. 삼각대 방향과 고도 나사로 망원경을 북극 쪽으로 조금 더 가깝게 향하게 한 뒤 다시 시도해 주세요.";
                continue;
            }
            var bg = sharpCap.Last?.Background;
            if (bg is null)
            {
                // 밝기를 모르면 예전처럼 늘리기만
                if (_exposure >= MaxExposure) return $"노출을 {MaxExposure:0}초까지 늘려도 위치를 찾지 못했습니다. 망원경이 북극에서 멀리 향해 있을 수 있습니다. 삼각대 방향과 고도 나사로 망원경을 북극 쪽으로 조금 더 가깝게 향하게 한 뒤 다시 시도해 주세요. 가이드 망원경 덮개와 구름도 확인해 주세요.";
                _exposure = Math.Min(MaxExposure, _exposure * 2);
            }
            else if (bg > Bright)
            {
                if (_exposure <= MinExposure)
                    return $"하늘 배경이 너무 밝습니다 (밝기 {bg:P0}, 노출 {MinExposure}초). 달빛·조명이 들어오지 않는지 보고, SharpCap에서 게인을 낮춘 뒤 다시 시도해 주세요.";
                _exposure = Math.Max(MinExposure, _exposure / 2);
            }
            else if (bg < Dark && _exposure < MaxExposure) _exposure = Math.Min(MaxExposure, _exposure * 2);
            else
                // 배경은 적당한데 못 찾음 → 노출 탓이 아님. 더 바꾸면 오히려 하얘진다.
                // 가장 흔한 원인은 조준: 가이드 시야(세로 약 1.5°)에 북극 주변이 들어와야 한다 (2026-10-09 실기 — 조준을 고치니 바로 찾음)
                return $"별은 잘 보이는데 SharpCap이 위치를 찾지 못했습니다. 망원경이 북극에서 멀리 향해 있을 수 있습니다. 삼각대 방향과 고도 나사로 망원경을 북극 쪽으로 조금 더 가깝게 향하게 한 뒤 다시 시도해 주세요. 그래도 안 되면 구름·창틀·건물에 가린 부분과 가이드 망원경 초점을 확인해 주세요.";
            sharpCap.Send($"exposure:{_exposure * 1000}");
        }
        return $"노출을 여러 번 바꿔도 위치를 찾지 못했습니다. 망원경이 북극에서 멀리 향해 있을 수 있습니다. 삼각대 방향과 고도 나사로 망원경을 북극 쪽으로 조금 더 가깝게 향하게 한 뒤 다시 시도해 주세요.";
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
