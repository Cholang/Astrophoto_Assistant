using System.Runtime.CompilerServices;

namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 촬영 준비 진행자 (docs/PREPARE_IMPLEMENTATION.md 2.3·2.4). 작업 내용은 모르고 다음만 맡는다:
/// 순서와 끝 버튼, 다시 하기 의존표, 끝 상태 약속 확인, 멈춤 확인(IMPL-01), 실행 번호로 늦은 갱신 차단·오래된 값 표시(IMPL-04),
/// 다시 하기 순서(중단 → 정지 확인 → 이전 결과 차단 → 무효화 → 재실행, IMPL-05). 상태는 서버가 갖고 화면은 그리기만 한다.
/// </summary>
public sealed class PrepareRunner(IEnumerable<IPrepTask> tasks, ILogger<PrepareRunner> log)
{
    /// <summary>다시 하면 "다시 확인 필요"가 되는 뒤 작업 (DESIGN.md "이전 단계로 돌아가기")</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Dependents = new Dictionary<string, string[]>
    {
        ["polar"] = ["calibration", "slew", "focus", "center", "guiding", "test"],
        ["calibration"] = ["slew", "center", "guiding", "test"],
        ["slew"] = ["center", "guiding", "test"],
        ["focus"] = ["test"],
        ["center"] = ["guiding", "test"],
        ["guiding"] = ["test"],
        ["test"] = [],
    };

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly IReadOnlyList<IPrepTask> _tasks = tasks.ToList();
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;

    private PrepContext? _ctx;
    private readonly Dictionary<string, (PrepTaskStatus Status, string? Result)> _rows = [];
    private Current? _cur;
    private int _runId;
    private CancellationTokenSource? _runCts;
    private Task _runTask = Task.CompletedTask;
    private bool _ready;
    /// <summary>정지를 확인하지 못한 작업과, 확인되면 이어서 할 일</summary>
    private (IPrepTask Task, Func<Task> Then)? _stopUnconfirmed;
    private Timer? _heartbeat;

    private sealed class Current(IPrepTask task, int runId)
    {
        public IPrepTask Task { get; } = task;
        public int RunId { get; } = runId;
        public int SubIndex { get; set; }
        public bool Finished { get; set; }
        public GuideView Guide { get; set; } = new(task.Title, "");
        public ReadoutView? Readout { get; set; }
        public bool ReadoutLive { get; set; }
        public StatusLine? Status { get; set; }
        public IReadOnlyList<PrepAction> Actions { get; set; } = [];
        public LiveView? Live { get; set; }
        public TaskCompletionSource<string>? Ask { get; set; }
    }

    public PrepContext? Context { get { lock (_gate) return _ctx; } }

    // ── 상태 읽기 ─────────

    public PrepView View()
    {
        lock (_gate) return Snapshot();
    }

    private PrepView Snapshot()
    {
        var rows = _tasks.Where(t => _rows.ContainsKey(t.Id))
            .Select(t => new TaskRowView(t.Id, t.Title, _rows[t.Id].Status, _rows[t.Id].Result)).ToList();
        CurrentView? cur = null;
        if (_cur is { } c)
        {
            var subs = c.Task.SubSteps.Select((s, i) => new SubStepView(s.Id, s.Label,
                c.Finished || i < c.SubIndex ? PrepTaskStatus.Done : i == c.SubIndex ? PrepTaskStatus.Running : PrepTaskStatus.Pending)).ToList();
            cur = new CurrentView(c.Task.Id, c.RunId, subs, c.Guide, new CenterView(c.Readout, c.Status, c.Actions), c.Live);
        }
        return new PrepView(_ctx is not null, rows, cur, _ready, _version);
    }

    /// <summary>상태가 바뀔 때마다 하나씩 (Server-Sent Events)</summary>
    public async IAsyncEnumerable<PrepView> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var last = -1;
        while (!ct.IsCancellationRequested)
        {
            PrepView view;
            Task changed;
            lock (_gate)
            {
                view = Snapshot();
                changed = _changed.Task;
            }
            if (view.Version != last)
            {
                last = view.Version;
                yield return view;
            }
            try { await changed.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>다음 변화까지 기다린다 (테스트용)</summary>
    public Task NextChangeAsync()
    {
        lock (_gate) return _changed.Task;
    }

    private void Changed()
    {
        // _gate 안에서 부른다
        _version++;
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    // ── 시작 ─────────

    /// <summary>확정 계획 하나로 준비를 시작한다. 같은 계획이면 하던 곳에서 그대로(화면 재접속). 준비 시작 = ① 바로 시작</summary>
    public void Start(PrepContext ctx)
    {
        lock (_gate)
        {
            if (_ctx is not null && _ctx.Plan.ConfirmedAt == ctx.Plan.ConfirmedAt) return;
        }
        _ = StartFreshAsync(ctx);
    }

    private async Task StartFreshAsync(PrepContext ctx)
    {
        // 새 계획: 하던 작업이 있으면 멈추고 정지를 확인한 뒤 시작
        if (!await StopCurrentAsync(() => StartFreshAsync(ctx))) return;
        lock (_gate)
        {
            _ctx = ctx;
            _ready = false;
            _cur = null;
            _rows.Clear();
            foreach (var t in _tasks)
                _rows[t.Id] = (t.AppliesTo(ctx) ? PrepTaskStatus.Pending : PrepTaskStatus.Skipped, null);
            _heartbeat ??= new Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Changed();
        }
        log.LogInformation("준비 시작: {Target}", ctx.Plan.TargetName);
        RunNext();
    }

    /// <summary>다음으로 할 작업(대기 또는 다시 확인 필요)을 시작한다. 없으면 준비 끝</summary>
    private void RunNext()
    {
        IPrepTask? next;
        lock (_gate)
        {
            next = _tasks.FirstOrDefault(t => _rows[t.Id].Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
        }
        if (next is not null) RunTask(next);
    }

    private void RunTask(IPrepTask task)
    {
        Current cur;
        CancellationToken ct;
        lock (_gate)
        {
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            ct = _runCts.Token;
            cur = new Current(task, ++_runId);
            _cur = cur;
            _rows[task.Id] = (PrepTaskStatus.Running, null);
            Changed();
        }
        var handle = new RunHandle(this, cur.RunId);
        _runTask = Task.Run(async () =>
        {
            try
            {
                var outcome = await task.RunAsync(handle, ct);
                await OnOutcomeAsync(cur, outcome);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                log.LogError(e, "준비 작업 오류: {Task}", task.Id);
                lock (_gate)
                {
                    if (_cur != cur) return;
                    _rows[task.Id] = (PrepTaskStatus.Failed, null);
                    cur.Status = new StatusLine("예상하지 못한 오류로 멈췄습니다. 다시 시도해 주세요.", Tone.Fail);
                    cur.Actions = [new("retry", "다시 시도", true)];
                    Changed();
                }
            }
        }, CancellationToken.None);
    }

    private async Task OnOutcomeAsync(Current cur, TaskOutcome outcome)
    {
        lock (_gate) if (_cur != cur) return; // 지난 실행
        switch (outcome)
        {
            case RedoRequest redo:
                // 제안한 작업도 끝나지 않았으니 다시 확인 필요 (앞 작업을 다시 한 뒤 차례가 오면 다시 한다)
                lock (_gate) _rows[cur.Task.Id] = (PrepTaskStatus.NeedsRecheck, null);
                _ = RedoAsync(redo.TaskId, fromRunning: false);
                return;
            case Completed done:
                _ctx!.Results.Set(done.Result);
                await FinishAsync(cur, done);
                return;
        }
    }

    /// <summary>끝 상태 약속을 장비에서 확인하고, 지켜졌으면 끝 버튼(다음 작업 시작)을 보인다</summary>
    private async Task FinishAsync(Current cur, Completed done)
    {
        lock (_gate)
        {
            if (_cur != cur) return;
            _pendingFinish = done; // 약속이 어긋나 "다시 확인"을 누르면 이것으로 다시
            cur.Status = new StatusLine("장비 상태를 확인하는 중입니다", Tone.Busy);
            cur.Actions = [];
            Changed();
        }
        EndStateCheck check;
        try { check = await cur.Task.CheckEndStateAsync(_ctx!, CancellationToken.None); }
        catch (Exception e) { check = EndStateCheck.Fail($"장비 상태를 읽지 못했습니다 ({e.Message})"); }
        lock (_gate)
        {
            if (_cur != cur) return;
            if (!check.Ok)
            {
                // 약속이 어긋나면 다음으로 넘기지 않는다
                _rows[cur.Task.Id] = (PrepTaskStatus.Failed, null);
                cur.Status = new StatusLine($"{cur.Task.Title}이 끝났지만 장비 상태가 맞지 않습니다: {check.Problem}", Tone.Fail);
                cur.Actions = [new("endcheck", "다시 확인", true), new($"redo:{cur.Task.Id}", "이 작업 다시 하기")];
                Changed();
                return;
            }
            cur.Finished = true;
            cur.Guide = new GuideView(done.GuideTitle, done.GuideText);
            cur.Status = null;
            _rows[cur.Task.Id] = (PrepTaskStatus.Done, done.ResultLine);
            var next = _tasks.FirstOrDefault(t => _rows[t.Id].Status is PrepTaskStatus.Pending or PrepTaskStatus.NeedsRecheck);
            var primary = next is null ? new PrepAction("start", "촬영 시작", true) : new PrepAction("next", next.StartLabel, true);
            cur.Actions = [primary, .. done.Extra ?? []];
            Changed();
        }
    }

    private Completed? _pendingFinish;

    // ── 사용자 버튼 ─────────

    /// <summary>버튼을 눌렀을 때. 받을 수 없으면 이유(사용자에게 보여 줄 문장)</summary>
    public string? Act(string actionId)
    {
        Current? cur;
        lock (_gate)
        {
            if (_ctx is null) return "준비가 시작되지 않았습니다.";
            cur = _cur;
            if (cur is null || cur.Actions.All(a => a.Id != actionId)) return "지금은 그 버튼을 쓸 수 없습니다.";
            if (cur.Ask is { } ask)
            {
                cur.Ask = null;
                cur.Actions = [];
                _rows[cur.Task.Id] = (PrepTaskStatus.Running, null);
                Changed();
                ask.TrySetResult(actionId);
                return null;
            }
        }
        switch (actionId)
        {
            case "next": RunNext(); break;
            case "start":
                lock (_gate) { _ready = true; Changed(); }
                log.LogInformation("준비 완료: 촬영 시작");
                break;
            case "retry": _ = RetryAsync(cur.Task); break;
            case "restart": RunNext(); break;
            case "endcheck":
                if (_pendingFinish is { } done) _ = FinishAsync(cur, done);
                break;
            case "device-check": _ = RecheckStopAsync(); break;
            default:
                if (actionId.StartsWith("redo:", StringComparison.Ordinal)) _ = RedoAsync(actionId[5..], fromRunning: true);
                else return "지금은 그 버튼을 쓸 수 없습니다.";
                break;
        }
        return null;
    }

    /// <summary>
    /// 다시 하기 (IMPL-05): 진행 중 작업 중단 → 정지 확인 → 이전 실행 결과 차단(RunId) → 영향받는 결과 무효화 → 재실행.
    /// 정지를 확인하지 못하면 "장비 상태 확인 필요"에서 멈춘다.
    /// </summary>
    public async Task RedoAsync(string taskId, bool fromRunning)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null) return;
        if (fromRunning && !await StopCurrentAsync(() => RedoAsync(taskId, fromRunning: false))) return;
        var affected = new[] { taskId }.Concat(Dependents.GetValueOrDefault(taskId, [])).ToList();
        // 무효가 되는 작업 중 끝난 뒤에도 장비가 계속 도는 것(가이딩)은 결과만 지우지 않고 실제로 멈춘다 (CX-PREP-CODE-01)
        if (!await StopLingeringAsync(affected, () => RedoAsync(taskId, fromRunning: false))) return;
        lock (_gate)
        {
            if (_ctx is null) return;
            foreach (var id in affected)
            {
                if (!_rows.TryGetValue(id, out var row) || row.Status == PrepTaskStatus.Skipped) continue;
                _rows[id] = (id == taskId ? PrepTaskStatus.Running : PrepTaskStatus.NeedsRecheck, null);
                if (ResultType(id) is { } type) _ctx.Results.Remove(type);
            }
            _ready = false;
            Changed();
        }
        RunTask(task);
    }

    /// <summary>
    /// 준비 중단: 하던 작업을 멈추고, 끝난 뒤에도 도는 작업(가이딩)도 멈춘 뒤 정지를 확인한다.
    /// 작업이 끝나 다음 버튼을 기다리는 중이어도 중단 상태가 된다 — 다음·촬영 시작 버튼을 지운다 (CX-PREP-CODE-03).
    /// "다시 시작"은 아직 안 끝난(또는 멈춰서 다시 확인이 필요한) 첫 작업부터.
    /// </summary>
    public async Task<bool> AbortAsync()
    {
        Func<Task> again = async () => await AbortAsync();
        if (!await StopCurrentAsync(again)) return false;
        if (!await StopLingeringAsync(_tasks.Select(t => t.Id).ToList(), again)) return false;
        MarkAborted();
        return true;
    }

    private void MarkAborted()
    {
        lock (_gate)
        {
            if (_ctx is null || _cur is not { } c) return;
            if (!c.Finished) _rows[c.Task.Id] = (PrepTaskStatus.Pending, null);
            c.Ask = null;
            c.Status = new StatusLine("준비를 멈췄습니다", Tone.Warn);
            c.Actions = [new("restart", "다시 시작", true)];
            _ready = false;
            Changed();
        }
    }

    /// <summary>
    /// 끝난 뒤에도 장비가 계속 도는 작업(KeepsRunning)을 멈추고 확인한다. 멈춘 작업과 그 뒤 작업은 "다시 확인 필요"로, 결과는 지운다.
    /// 확인하지 못하면 "장비 상태 다시 확인"에서 멈추고(then을 기억) false.
    /// </summary>
    private async Task<bool> StopLingeringAsync(IReadOnlyList<string> ids, Func<Task> then)
    {
        List<IPrepTask> lingering;
        lock (_gate)
        {
            if (_ctx is null) return true;
            lingering = _tasks.Where(t => t.KeepsRunning && ids.Contains(t.Id)
                && _rows.TryGetValue(t.Id, out var r) && r.Status is PrepTaskStatus.Done or PrepTaskStatus.Failed).ToList();
        }
        foreach (var task in lingering)
        {
            if (!await TryStopAsync(task))
            {
                ShowStopUnconfirmed(task, then);
                return false;
            }
            lock (_gate)
            {
                foreach (var id in new[] { task.Id }.Concat(Dependents.GetValueOrDefault(task.Id, [])))
                {
                    if (!_rows.TryGetValue(id, out var row) || row.Status is PrepTaskStatus.Skipped or PrepTaskStatus.Pending) continue;
                    _rows[id] = (PrepTaskStatus.NeedsRecheck, null);
                    if (ResultType(id) is { } type) _ctx!.Results.Remove(type);
                }
                _ready = false;
                Changed();
            }
        }
        return true;
    }

    /// <summary>예외로 멈춘 작업 "다시 시도": 장비가 멈췄는지 먼저 확인한 뒤 (CX-PREP-CODE-02)</summary>
    private async Task RetryAsync(IPrepTask task)
    {
        if (!await StopCurrentAsync(() => RetryAsync(task))) return;
        RunTask(task);
    }

    /// <summary>
    /// 진행 중 작업을 취소하고 장비 정지를 확인한다 (IMPL-01). 정지 호출은 취소된 토큰과 무관한 별도 토큰으로.
    /// 확인 못 하면 then을 기억해 두고 false — 사용자가 "장비 상태 다시 확인"을 누르면 다시 확인 후 then.
    /// </summary>
    private async Task<bool> StopCurrentAsync(Func<Task> then)
    {
        Current? cur;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cur = _cur;
            cts = _runCts;
        }
        if (cur is null) return true;
        var wasRunning = !_runTask.IsCompleted;
        cts?.Cancel();
        try { await _runTask.WaitAsync(StopTimeout); } catch { /* 취소·시간 초과 — 아래에서 정지 확인 */ }
        if (!wasRunning && cur.Finished) return true; // 끝난 작업은 움직이는 장비가 없다
        if (await TryStopAsync(cur.Task)) return true;
        ShowStopUnconfirmed(cur.Task, then);
        return false;
    }

    /// <summary>정지를 확인하지 못함: 다음 동작을 막고 "장비 상태 다시 확인"만 (확인되면 then)</summary>
    private void ShowStopUnconfirmed(IPrepTask task, Func<Task> then)
    {
        lock (_gate)
        {
            _stopUnconfirmed = (task, then);
            if (_cur is not { } c) return;
            c.Ask = null;
            c.Status = new StatusLine($"{task.Title}을 멈췄지만 장비가 멈췄는지 확인하지 못했습니다. 장비 상태를 확인해 주세요.", Tone.Fail);
            c.Actions = [new("device-check", "장비 상태 다시 확인", true)];
            _rows[task.Id] = (PrepTaskStatus.Failed, null);
            _ready = false;
            Changed();
        }
    }

    private async Task<bool> TryStopAsync(IPrepTask task)
    {
        using var cts = new CancellationTokenSource(StopTimeout);
        try { return await task.StopAsync(cts.Token); }
        catch (Exception e)
        {
            log.LogWarning(e, "정지 확인 실패: {Task}", task.Id);
            return false;
        }
    }

    private async Task RecheckStopAsync()
    {
        (IPrepTask Task, Func<Task> Then)? pending;
        lock (_gate) pending = _stopUnconfirmed;
        if (pending is not { } p) return;
        if (!await TryStopAsync(p.Task)) return;
        lock (_gate)
        {
            _stopUnconfirmed = null;
            if (_cur is { } c)
            {
                c.Status = new StatusLine("장비가 멈춘 것을 확인했습니다", Tone.Ok);
                c.Actions = [];
                Changed();
            }
        }
        await p.Then();
    }

    private static Type? ResultType(string taskId) => taskId switch
    {
        "polar" => typeof(PolarResult),
        "calibration" => typeof(CalibrationResult),
        "slew" => typeof(SlewResult),
        "focus" => typeof(FocusResult),
        "center" => typeof(CenterResult),
        "guiding" => typeof(GuidingResult),
        "test" => typeof(TestShotResult),
        _ => null,
    };

    // ── 오래된 값 표시 (IMPL-04) ─────────

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_cur is not { ReadoutLive: true, Readout: { Freshness: Freshness.Fresh } r } c) return;
            if (DateTimeOffset.Now - r.ObservedAt <= StaleAfter) return;
            c.Readout = r with { Freshness = Freshness.Stale };
            Changed();
        }
    }

    /// <summary>[테스트] 시간이 지났다고 보고 오래된 값 검사를 바로 한다</summary>
    internal void CheckStaleNow(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_cur is not { ReadoutLive: true, Readout: { Freshness: Freshness.Fresh } r } c) return;
            if (now - r.ObservedAt <= StaleAfter) return;
            c.Readout = r with { Freshness = Freshness.Stale };
            Changed();
        }
    }

    // ── 작업이 화면을 갱신하는 통로 (지난 실행의 갱신은 버린다) ─────────

    private bool Update(int runId, Action<Current> change)
    {
        lock (_gate)
        {
            if (_cur is not { } c || c.RunId != runId) return false;
            change(c);
            Changed();
            return true;
        }
    }

    private sealed class RunHandle(PrepareRunner r, int runId) : ITaskRun
    {
        public PrepContext Context => r._ctx!;
        public int RunId => runId;

        public void SubStep(int index) => r.Update(runId, c => c.SubIndex = index);
        public void Guide(string title, string text) => r.Update(runId, c => c.Guide = new GuideView(title, text));

        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) =>
            r.Update(runId, c =>
            {
                c.Readout = new ReadoutView(kind, big, caption, tone, values ?? new Dictionary<string, double>(), observedAt ?? DateTimeOffset.Now,
                    unverified ? Freshness.Unverified : Freshness.Fresh);
                c.ReadoutLive = live;
            });

        public void ClearReadout() => r.Update(runId, c => { c.Readout = null; c.ReadoutLive = false; });
        public void Status(string? text, Tone tone = Tone.Busy) => r.Update(runId, c => c.Status = text is null ? null : new StatusLine(text, tone));
        public void Live(string kind, string? url = null, object? data = null) => r.Update(runId, c => c.Live = new LiveView(kind, url, data, DateTimeOffset.Now));

        public async Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!r.Update(runId, c =>
                {
                    c.Ask?.TrySetCanceled();
                    c.Ask = tcs;
                    c.Actions = actions;
                    r._rows[c.Task.Id] = (PrepTaskStatus.Waiting, null);
                }))
                throw new OperationCanceledException();
            // 취소되면(작업이 다른 길로 감) 그 버튼을 화면에서 바로 치운다
            await using var reg = ct.Register(() =>
            {
                if (tcs.TrySetCanceled(ct))
                    r.Update(runId, c => { if (c.Ask == tcs) { c.Ask = null; c.Actions = []; } });
            });
            return await tcs.Task;
        }
    }
}
