using Astro.Core.Sky;
using Astro.Server.Assistant;

namespace Astro.Server.Prepare.Flow;

// 작업 계약 (docs/PREPARE_IMPLEMENTATION.md 2.3 "작업 분리"):
// 작업은 이 계약만 지키고, 다른 작업을 직접 부르거나 참조하지 않는다. 앞 작업 결과는 PrepResults의 기록으로만 읽는다.
// 화면 갱신은 ITaskRun으로만 하고, 끝나면 결과 기록 하나를 남긴다. 러너가 끝 상태 약속을 장비에서 확인한 뒤 다음으로 넘긴다.

/// <summary>준비 작업 하나 (극축 정렬 … 시험 사진)</summary>
public interface IPrepTask
{
    string Id { get; }
    string Title { get; }
    /// <summary>이 작업을 시작하는 버튼 이름 (앞 작업의 끝 버튼). 예: "캘리브레이션 시작"</summary>
    string StartLabel { get; }
    IReadOnlyList<SubStep> SubSteps { get; }

    /// <summary>이번 준비에서 할 작업인가 (예: 가이더 없으면 캘리브레이션·가이딩은 건너뜀)</summary>
    bool AppliesTo(PrepContext ctx);

    /// <summary>작업을 끝까지 진행한다. 안에서의 실패·재시도는 작업이 처리하고, 끝내 안 되면 ITaskRun.AskAsync로 사용자에게 묻는다</summary>
    Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct);

    /// <summary>끝 상태 약속을 실제 장비에서 읽어 확인한다 (러너가 다음 작업으로 넘기기 전에)</summary>
    Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct);

    /// <summary>움직이던 장비를 멈추고 멈춘 것을 확인한다 (CX-PREP-IMPL-01). 확인하지 못하면 false</summary>
    Task<bool> StopAsync(CancellationToken ct);
}

public sealed record SubStep(string Id, string Label);

/// <summary>끝 상태 약속 확인 결과. Ok가 아니면 Problem에 사용자에게 보여 줄 문장</summary>
public sealed record EndStateCheck(bool Ok, string? Problem = null)
{
    public static readonly EndStateCheck Pass = new(true);
    public static EndStateCheck Fail(string problem) => new(false, problem);
}

/// <summary>작업이 끝난 방식</summary>
public abstract record TaskOutcome;

/// <summary>
/// 끝남. Result = 결과 기록(뒤 작업이 읽음), ResultLine = 진행 표시의 결과 한 줄, GuideTitle = 끝난 뒤 제목 줄의 결과 말,
/// GuideText = 의미·다음 할 일(숫자 없음), Extra = 끝 버튼 옆 보조 버튼 ("redo:작업id"면 러너가 그 작업을 다시 함)
/// </summary>
public sealed record Completed(object Result, string ResultLine, string GuideTitle, string GuideText, IReadOnlyList<PrepAction>? Extra = null) : TaskOutcome;

/// <summary>다른 작업부터 다시 하자고 러너에 제안 (예: 센터링이 안 되면 초점부터). 러너가 의존표대로 처리</summary>
public sealed record RedoRequest(string TaskId) : TaskOutcome;

/// <summary>작업이 화면을 갱신하고 사용자에게 묻는 통로. 러너가 실행 번호(RunId)를 붙여 지난 실행의 늦은 갱신은 버린다</summary>
public interface ITaskRun
{
    PrepContext Context { get; }
    int RunId { get; }
    void SubStep(int index);
    void Guide(string title, string text);
    /// <summary>측정값 칸. live = 계속 들어오는 값(끊기면 Stale로 표시), unverified = 단위·환산 미검증 값</summary>
    void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false);
    void ClearReadout();
    void Status(string? text, Tone tone = Tone.Busy);
    void Live(string kind, string? url = null, object? data = null);
    /// <summary>버튼을 보여 주고 사용자가 누른 버튼 id를 기다린다</summary>
    Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct);
}

/// <summary>준비 한 번(확정 계획 하나)의 상황. 작업들이 같이 읽는다</summary>
public sealed class PrepContext(
    PreparationPlan plan, Site site, double mainPixelScaleArcsec, bool hasGuider, bool hasFocuser,
    PrepResults results, MountLock mount, IPrepMemory memory, Func<DateTimeOffset> clock)
{
    public PreparationPlan Plan { get; } = plan;
    public Site Site { get; } = site;
    /// <summary>주 카메라 한 픽셀이 담는 하늘 크기(″) — 가이딩 판정 기준</summary>
    public double MainPixelScaleArcsec { get; } = mainPixelScaleArcsec;
    public bool HasGuider { get; } = hasGuider;
    public bool HasFocuser { get; } = hasFocuser;
    public PrepResults Results { get; } = results;
    public MountLock Mount { get; } = mount;
    public IPrepMemory Memory { get; } = memory;
    public Func<DateTimeOffset> Now { get; } = clock;

    /// <summary>⑦에서 노출을 바꾸면 계획 값 대신 이 값 (계획에 반영)</summary>
    public int ExposureSeconds { get; set; } = plan.ExposureSeconds;
    /// <summary>[모의] 낮에 흐름을 볼 때 대상이 보인다고 가정</summary>
    public bool IgnoreAltitude { get; set; }

    public string TargetName => Plan.TargetKoreanName is { } k ? $"{Plan.TargetName} {k}" : Plan.TargetName;

    /// <summary>대상의 지금 고도(도)</summary>
    public double TargetAltitude(DateTimeOffset when) =>
        Astronomy.Altitude(new Equatorial(Plan.RaDegrees, Plan.DecDegrees), Site, when.UtcDateTime);
}

/// <summary>장비 명령의 결과. 실패면 Problem에 사용자에게 보여 줄 문장</summary>
public sealed record DeviceResult(bool Ok, string? Problem = null)
{
    public static readonly DeviceResult Success = new(true);
    public static DeviceResult Fail(string problem) => new(false, problem);
}

/// <summary>
/// 적도의 명령은 한 번에 하나 (CX-PREP-IMPL-02). 적도의를 움직이는 작업은 잠금을 잡은 동안에만 명령을 보낸다.
/// 사용자가 다른 앱에서 직접 조작하는 것까지 막지는 못한다.
/// </summary>
public sealed class MountLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    public bool IsHeld => _gate.CurrentCount == 0;

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _done;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>밤을 넘어 기억할 것 (지난 초점 위치·기온, 대상별 카메라 방향). P1은 메모리, 나중에 AA 데이터 폴더</summary>
public interface IPrepMemory
{
    (int Position, double? TemperatureC)? LastFocus { get; set; }
    /// <summary>그날 밤 마지막 캘리브레이션 (같은 밤 대상만 바꾸면 재사용)</summary>
    CalibrationResult? LastCalibration { get; set; }
    double? CameraAngle(string targetId);
    void SetCameraAngle(string targetId, double degrees);
}

public sealed class InMemoryPrepMemory : IPrepMemory
{
    private readonly Dictionary<string, double> _angles = [];
    public (int Position, double? TemperatureC)? LastFocus { get; set; }
    public CalibrationResult? LastCalibration { get; set; }
    public double? CameraAngle(string targetId) => _angles.TryGetValue(targetId, out var a) ? a : null;
    public void SetCameraAngle(string targetId, double degrees) => _angles[targetId] = degrees;
}
