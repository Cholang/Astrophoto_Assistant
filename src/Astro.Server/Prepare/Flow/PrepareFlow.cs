namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 장비 준비 · 대상 두 묶음 (DESIGN.md "단계 재구성", 2026-10-07 결정). 러너 코드는 같고 작업 목록·설정만 다르다.
/// 묶음 사이의 일을 맡는다:
/// - 결과 기록은 그날 밤 두 묶음이 함께 쓴다 — 대상을 바꿔도 장비 준비 결과 유지 (밤이 바뀌면 새로)
/// - 대상 묶음은 장비 준비가 모두 끝났을 때만 시작
/// - 장비 준비 작업을 (다시) 할 때는 대상 묶음을 먼저 멈춰 두고(가이딩 정지 확인 포함) 영향받는 작업을 "다시 확인 필요"로
/// - 대상 묶음이 장비 준비 작업을 다시 하자고 하면(가이딩 → 캘리브레이션) 대상을 멈춰 두고 장비 준비로 넘긴다.
///   장비 준비가 다시 끝나 같은 계획으로 대상을 시작하면 다시 확인할 첫 작업부터 이어서 한다
/// </summary>
public sealed class PrepareFlow
{
    /// <summary>장비 준비 작업을 다시 하면 대상 묶음에서 다시 확인할 작업. 극축·캘리브레이션은 적도의를 옮기므로 전부</summary>
    public static readonly IReadOnlyDictionary<string, string[]> CrossDependents = new Dictionary<string, string[]>
    {
        ["polar"] = ["slew", "center", "focuscheck", "guiding", "test"],
        ["calibration"] = ["slew", "center", "focuscheck", "guiding", "test"],
        ["focus"] = ["focuscheck", "test"],
    };

    public static readonly string[] RigTaskIds = ["polar", "calibration", "focus"];
    /// <summary>마무리 묶음 (DESIGN.md "마무리")</summary>
    public static readonly string[] WrapTaskIds = ["flat", "dark", "pack"];

    private readonly Lock _gate = new();
    private PrepResults _results = new();
    private DateOnly? _evening;

    public PrepareRunner Rig { get; }
    public PrepareRunner Target { get; }
    public PrepareRunner Wrap { get; }

    public PrepareFlow(IEnumerable<IPrepTask> tasks, ILogger<PrepareRunner> log, bool simulated, TimeSpan? autoNextDelay = null)
    {
        var all = tasks.ToList();
        var delay = autoNextDelay ?? TimeSpan.FromSeconds(1.8);
        Rig = new PrepareRunner(all.Where(t => RigTaskIds.Contains(t.Id)), log, RunnerSetup.Rig) { Simulated = simulated, AutoNextDelay = delay };
        Target = new PrepareRunner(all.Where(t => !RigTaskIds.Contains(t.Id) && !WrapTaskIds.Contains(t.Id)), log, RunnerSetup.Target) { Simulated = simulated, AutoNextDelay = delay };
        Wrap = new PrepareRunner(all.Where(t => WrapTaskIds.Contains(t.Id)), log, RunnerSetup.Wrap) { Simulated = simulated, AutoNextDelay = delay };

        // 장비 준비 작업이 돌기 전: 대상 묶음이 장비를 쓰고 있으면 멈춰 두고 영향받는 작업을 다시 확인 필요로
        Rig.BeforeRun = id => Target.SuspendAsync(CrossDependents.GetValueOrDefault(id, []), "장비 준비를 다시 하는 중이에요", handoff: "rig");
        // 대상 묶음이 장비 준비 작업을 다시 하자고 함 → 대상을 멈춰 두고 장비 준비에서 다시
        Target.ForeignRedo = async id =>
        {
            if (!RigTaskIds.Contains(id)) return;
            // 요청한 작업(가이딩)의 실행이 먼저 끝나게 한 뒤 멈춘다 (자기 실행을 기다리지 않게)
            await Task.Yield();
            if (!await Target.SuspendAsync(CrossDependents.GetValueOrDefault(id, []), "장비 준비를 다시 하는 중이에요", handoff: "rig")) return;
            await Rig.RedoAsync(id, fromRunning: false);
        };
    }

    /// <summary>그날 밤 결과 기록 (두 묶음이 함께). 밤이 바뀌면 새로 만든다</summary>
    public PrepResults ResultsFor(DateOnly evening)
    {
        lock (_gate)
        {
            if (_evening != evening)
            {
                _evening = evening;
                _results = new PrepResults();
            }
            return _results;
        }
    }

    /// <summary>장비 준비 시작 (그날 밤 하나 — 같은 밤이면 하던 곳에서 그대로)</summary>
    public void StartRig(PrepContext ctx, DateOnly evening) => Rig.Start(ctx, $"rig:{evening:yyyy-MM-dd}");

    /// <summary>대상 시작 (확정 계획 하나). 장비 준비가 끝나지 않았으면 이유</summary>
    public string? StartTarget(PrepContext ctx)
    {
        if (!Rig.AllDone) return "장비 준비가 아직 끝나지 않았습니다. 장비 준비를 먼저 끝내 주세요.";
        Target.Start(ctx);
        return null;
    }

    /// <summary>마무리 시작 (그날 밤 하나 — 같은 밤이면 하던 곳에서 그대로). 촬영이 끝난 뒤</summary>
    public void StartWrap(PrepContext ctx, DateOnly evening) => Wrap.Start(ctx, $"wrap:{evening:yyyy-MM-dd}");

    public PrepareRunner? Runner(string group) => group switch
    {
        "rig" => Rig,
        "target" => Target,
        "wrap" => Wrap,
        _ => null,
    };
}
