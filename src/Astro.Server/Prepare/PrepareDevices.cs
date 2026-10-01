namespace Astro.Server.Prepare;

/// <summary>한 단계의 결과. 실패면 Problem에 사용자에게 보여 줄 문장</summary>
public sealed record DeviceResult(bool Ok, string? Problem = null)
{
    public static readonly DeviceResult Success = new(true);
    public static DeviceResult Fail(string problem) => new(false, problem);
}

public sealed record FocusResult(bool Ok, double? Hfr, string? Problem = null);

/// <summary>센터링 결과: 마지막 오차(분), 솔빙으로 알게 된 지금 카메라 방향(도)</summary>
public sealed record CenterResult(bool Ok, double? ErrorArcmin, double? RotationDegrees, string? Problem = null);

/// <summary>가이딩 측정: 전체·적경·적위 오차(″), 측정한 횟수, 별을 잃은 횟수</summary>
public sealed record GuidingStats(double TotalArcsec, double RaArcsec, double DecArcsec, int Samples, int StarLost);

public sealed record GuidingResult(bool Ok, GuidingStats? Stats, string? Problem = null);

public sealed record TestShotResult(bool Ok, int? Stars, double? Hfr, string? ImageUrl, string? Problem = null);

/// <summary>가이딩 진행 중 단계 (화면에 차례로 보여 준다)</summary>
public enum GuidingPhase { SelectingStar, Calibrating, Settling, Measuring }

/// <summary>
/// 준비 단계가 장비에 시키는 일 (DESIGN.md 3장 "촬영 준비"). 준비 흐름(PrepareRunner)은 이것만 부르고,
/// 실제 장비(N.I.N.A. Advanced API · PHD2 · SharpCap)인지 모의인지는 모른다.
/// 실제 구현은 W4~W6에서 단계별로 채운다. 그 전까지는 SimulatedPrepareDevices.
/// </summary>
public interface IPrepareDevices
{
    /// <summary>PHD2가 찍던 것을 멈추고 가이드 카메라 연결을 끊어 다른 프로그램(SharpCap)이 쓸 수 있게 한다</summary>
    Task<DeviceResult> ReleaseGuideCameraAsync(CancellationToken ct);

    /// <summary>극축정렬 프로그램(SharpCap)을 연다. 창이 닫히면 onExited를 부른다. 못 열면 실패(사용자가 직접 열도록 안내)</summary>
    Task<DeviceResult> OpenPolarAlignToolAsync(Action onExited, CancellationToken ct);

    /// <summary>PHD2에 가이드 카메라를 다시 연결한다 (한 번 시도)</summary>
    Task<DeviceResult> ReconnectGuideCameraAsync(CancellationToken ct);

    Task<bool> HasFocuserAsync(CancellationToken ct);

    /// <summary>좌표(J2000, 도)로 이동만 한다 (센터링은 따로)</summary>
    Task<DeviceResult> SlewAsync(double raDegrees, double decDegrees, CancellationToken ct);

    Task<FocusResult> AutofocusAsync(CancellationToken ct);

    /// <summary>사진 → 솔빙 → 위치 보정을 반복해 가운데로. 카메라 방향은 돌리지 않는다</summary>
    Task<CenterResult> CenterAsync(double raDegrees, double decDegrees, CancellationToken ct);

    /// <summary>가이딩을 시작해 안정될 때까지 기다린 뒤 일정 시간 오차를 잰다. 단계가 바뀔 때마다 onPhase</summary>
    Task<GuidingResult> StartGuidingAndMeasureAsync(bool calibrate, Action<GuidingPhase> onPhase, CancellationToken ct);

    /// <summary>이미 가이딩 중일 때 오차만 다시 잰다</summary>
    Task<GuidingResult> MeasureGuidingAsync(CancellationToken ct);

    /// <summary>계획한 노출·ISO로 한 장 찍는다</summary>
    Task<TestShotResult> TestShotAsync(int exposureSeconds, int iso, CancellationToken ct);
}

/// <summary>
/// 모의 장비: 모두 "연결됐고, 정상 완료했고, 성공했다"고 가정한다 (2026-10-01 사용자 결정 — 장비 없이 흐름·화면부터).
/// 시간은 짧게 줄인다. 화면 확인용으로 다음 동작 하나를 실패시킬 수 있다 (FailNext).
/// </summary>
public sealed class SimulatedPrepareDevices : IPrepareDevices
{
    private volatile string? _failNext;

    /// <summary>[임시] 다음에 부르는 동작 하나를 실패시킨다 (단계 id: polar, reconnect, move, focus, center, guiding, test)</summary>
    public void FailNext(string step) => _failNext = step;

    private bool Failing(string step)
    {
        if (_failNext != step) return false;
        _failNext = null;
        return true;
    }

    public async Task<DeviceResult> ReleaseGuideCameraAsync(CancellationToken ct)
    {
        await Task.Delay(800, ct);
        return Failing("polar") ? DeviceResult.Fail("PHD2가 가이드 카메라를 놓지 않았습니다.") : DeviceResult.Success;
    }

    public Task<DeviceResult> OpenPolarAlignToolAsync(Action onExited, CancellationToken ct)
    {
        // 사용자가 SharpCap에서 정렬하고 창을 닫는 것을 흉내: 몇 초 뒤 닫힘
        _ = Task.Delay(5000, ct).ContinueWith(t => { if (!t.IsCanceled) onExited(); }, TaskScheduler.Default);
        return Task.FromResult(DeviceResult.Success);
    }

    public async Task<DeviceResult> ReconnectGuideCameraAsync(CancellationToken ct)
    {
        await Task.Delay(1200, ct);
        return Failing("reconnect") ? DeviceResult.Fail("가이드 카메라가 아직 다른 프로그램에 잡혀 있습니다.") : DeviceResult.Success;
    }

    public Task<bool> HasFocuserAsync(CancellationToken ct) => Task.FromResult(true);

    public async Task<DeviceResult> SlewAsync(double ra, double dec, CancellationToken ct)
    {
        await Task.Delay(2500, ct);
        return Failing("move") ? DeviceResult.Fail("적도의가 이동을 마치지 못했습니다.") : DeviceResult.Success;
    }

    public async Task<FocusResult> AutofocusAsync(CancellationToken ct)
    {
        await Task.Delay(3000, ct);
        return Failing("focus") ? new FocusResult(false, null, "자동초점에서 별을 찾지 못했습니다.") : new FocusResult(true, 2.1);
    }

    public async Task<CenterResult> CenterAsync(double ra, double dec, CancellationToken ct)
    {
        await Task.Delay(3000, ct);
        return Failing("center") ? new CenterResult(false, null, null, "사진에서 위치를 계산하지 못했습니다 (솔빙 실패).") : new CenterResult(true, 0.4, 87);
    }

    public async Task<GuidingResult> StartGuidingAndMeasureAsync(bool calibrate, Action<GuidingPhase> onPhase, CancellationToken ct)
    {
        onPhase(GuidingPhase.SelectingStar);
        await Task.Delay(1500, ct);
        if (Failing("guiding")) return new GuidingResult(false, null, "가이드 별을 찾지 못했습니다.");
        if (calibrate)
        {
            onPhase(GuidingPhase.Calibrating);
            await Task.Delay(2500, ct);
        }
        onPhase(GuidingPhase.Settling);
        await Task.Delay(1500, ct);
        return await MeasureGuidingAsync(onPhase, ct);
    }

    public Task<GuidingResult> MeasureGuidingAsync(CancellationToken ct) => MeasureGuidingAsync(_ => { }, ct);

    private static async Task<GuidingResult> MeasureGuidingAsync(Action<GuidingPhase> onPhase, CancellationToken ct)
    {
        onPhase(GuidingPhase.Measuring);
        await Task.Delay(2500, ct);
        return new GuidingResult(true, new GuidingStats(0.9, 0.6, 0.7, 60, 0));
    }

    public async Task<TestShotResult> TestShotAsync(int exposureSeconds, int iso, CancellationToken ct)
    {
        await Task.Delay(3000, ct);
        return Failing("test") ? new TestShotResult(false, null, null, null, "사진을 받지 못했습니다.") : new TestShotResult(true, 312, 2.3, null);
    }
}
