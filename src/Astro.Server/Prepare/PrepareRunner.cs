using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Astro.Core;
using Astro.Core.Sky;
using Astro.Server.Assistant;
using Astro.Server.Sky;

namespace Astro.Server.Prepare;

/// <summary>준비 칸 상태. Waiting = 사용자가 할 일이 있음(버튼)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrepStatus>))]
public enum PrepStatus { Pending, Running, Waiting, Pass, Fail, Skipped }

/// <summary>칸 아래에 보여 줄 버튼. Primary는 주 버튼(하나만)</summary>
public sealed record PrepAction(string Id, string Label, bool Primary = false);

public sealed record PrepStepView(
    string Id, string Title, string? Term, PrepStatus Status, string Hint, string Message,
    IReadOnlyList<PrepAction> Actions, object? Detail);

/// <summary>화면에 보내는 준비 상태 전체. Ready = 사용자가 "촬영 시작"을 눌렀다</summary>
public sealed record PrepView(bool Started, string? Current, IReadOnlyList<PrepStepView> Steps, bool Ready, int Version);

/// <summary>
/// 촬영 준비 (DESIGN.md 3장 "촬영 준비", flow.mmd ③). 확정 계획(PreparationPlan)을 받아 7칸을 차례로 진행한다:
/// 극축정렬(SharpCap, 사용자) → 가이드 연결 → [이동 승인 한 번] 대상 이동 → 초점 → 센터링 → 가이딩(사용자 확정) → 시험 사진(사용자 확인).
/// 상태는 서버가 갖고 화면은 받아서 그리기만 한다 (화면을 다시 열어도 이어서). 장비 동작은 IPrepareDevices가 한다.
/// </summary>
public sealed class PrepareRunner(IPrepareDevices devices, PlanAssistant planner, Engine.EquipmentChoices equipment, ILogger<PrepareRunner> log)
{
    private sealed class Step(string id, string title, string? term, string hint)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public string? Term { get; set; } = term;
        public string Hint { get; } = hint;
        public PrepStatus Status { get; set; } = PrepStatus.Pending;
        public string Message { get; set; } = "";
        public IReadOnlyList<PrepAction> Actions { get; set; } = [];
        public object? Detail { get; set; }
    }

    private const double UsableAltitude = 30;

    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;
    private CancellationTokenSource _cts = new();
    private Step[] _steps = [];
    private PreparationPlan? _plan;
    private NightContext? _night;
    private bool _guiding;
    private bool _busy; // 자동으로 진행 중인 동작이 있음 (그동안 버튼은 받지 않는다)
    private bool _ready;
    // 극축정렬: 사용자 확인과 SharpCap 종료를 둘 다 받아야 다음으로 (순서는 상관없음)
    private bool _paConfirmed, _toolExited, _toolTracked;
    // 캘리브레이션은 그날 밤 한 번 (극축정렬 뒤), 같은 밤 대상 변경엔 재사용 (2026-10-01 결정)
    private DateOnly? _calibratedEvening;
    private bool _ignoreAltitude; // [임시] 낮에 회사에서 흐름을 볼 때 "대상이 보인다고 가정"

    // ── 상태 읽기 ─────────

    public PrepView View()
    {
        lock (_gate) return Snapshot();
    }

    private PrepView Snapshot()
    {
        var current = _steps.FirstOrDefault(s => s.Status is PrepStatus.Running or PrepStatus.Waiting or PrepStatus.Fail)
            ?? _steps.LastOrDefault(s => s.Status == PrepStatus.Pass);
        return new PrepView(_plan is not null, current?.Id,
            _steps.Select(s => new PrepStepView(s.Id, s.Title, s.Term, s.Status, s.Hint, s.Message, s.Actions, s.Detail)).ToList(),
            _ready, _version);
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

    private void Changed()
    {
        // _gate 안에서 부른다
        _version++;
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    private Step S(string id) => _steps.First(s => s.Id == id);

    private void Set(string id, PrepStatus status, string message, IReadOnlyList<PrepAction>? actions = null, object? detail = null)
    {
        lock (_gate)
        {
            var s = S(id);
            s.Status = status;
            s.Message = message;
            s.Actions = actions ?? [];
            s.Detail = detail;
            Changed();
        }
    }

    // ── 시작 ─────────

    /// <summary>확정 계획으로 준비를 시작한다. 같은 계획이면 하던 곳에서 그대로 (화면을 다시 열었을 때)</summary>
    public string? Start()
    {
        if (planner.Confirmed is not { } plan) return "확정된 계획이 없습니다. 촬영 계획에서 \"이 계획으로 준비 시작\"을 눌러 주세요.";
        if (planner.Night is not { } night) return "오늘 밤 정보를 읽지 못했습니다. 촬영 계획 화면을 다시 열어 주세요.";
        lock (_gate)
        {
            if (_plan?.ConfirmedAt == plan.ConfirmedAt) return null;
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            _plan = plan;
            _night = night;
            _guiding = night.Rig.HasGuider && !equipment.IsWithout("guider");
            _ready = false;
            _busy = false;
            _paConfirmed = _toolExited = _toolTracked = false;
            var name = TargetName(plan);
            _steps =
            [
                new("polar", "극축정렬", "SharpCap", "가이드 카메라로 적도의의 회전축을 북극에 맞춥니다. 이 단계는 SharpCap에서 직접 합니다."),
                new("reconnect", "가이드 연결", "PHD2", "극축정렬에 썼던 가이드 카메라를 PHD2에 다시 연결합니다."),
                new("move", "대상 이동", plan.TargetName, $"적도의를 {name} 쪽으로 돌립니다. 이동을 누르면 초점·센터링까지 이어서 진행합니다."),
                new("focus", "초점", "자동초점", "대상 근처의 별로 주 카메라 초점을 맞춥니다."),
                new("center", "센터링", "N.I.N.A.", "사진을 찍어 실제 위치를 계산하고 대상을 화면 가운데로 맞춥니다. 카메라 방향은 그대로 둡니다."),
                new("guiding", "가이딩", "PHD2", "가이드 별을 따라 적도의를 미세하게 보정합니다. 안정되면 오차를 재어 보여 드립니다."),
                new("test", "시험 사진", $"{plan.ExposureSeconds}초", "계획한 노출로 한 장 찍어 초점·추적·구도를 확인합니다."),
            ];
            if (!_guiding)
            {
                foreach (var id in new[] { "reconnect", "guiding" })
                {
                    S(id).Status = PrepStatus.Skipped;
                    S(id).Message = "가이딩 없이 진행합니다.";
                }
            }
            Changed();
            log.LogInformation("준비 시작: {Target}", plan.TargetName);
        }
        Run(PolarAsync);
        return null;
    }

    /// <summary>자동 동작 하나를 뒤에서 돌린다. 도는 동안 버튼은 받지 않는다</summary>
    private void Run(Func<CancellationToken, Task> flow)
    {
        CancellationToken ct;
        lock (_gate)
        {
            if (_busy) return;
            _busy = true;
            ct = _cts.Token;
            Changed();
        }
        _ = Task.Run(async () =>
        {
            try { await flow(ct); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                log.LogError(e, "준비 단계 오류");
                lock (_gate)
                {
                    if (_steps.FirstOrDefault(s => s.Status == PrepStatus.Running) is { } running)
                    {
                        running.Status = PrepStatus.Fail;
                        running.Message = "예상하지 못한 오류로 멈췄습니다. 다시 시도해 주세요.";
                        running.Actions = [Retry];
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _busy = false;
                    Changed();
                }
            }
        }, CancellationToken.None);
    }

    private static readonly PrepAction Retry = new("retry", "다시 시도", true);

    // ── 사용자 버튼 ─────────

    /// <summary>칸의 버튼을 눌렀을 때. 받을 수 없으면 이유</summary>
    public string? Act(string step, string action)
    {
        lock (_gate)
        {
            if (_plan is null) return "준비가 시작되지 않았습니다.";
            if (_busy) return "진행 중인 동작이 끝난 뒤에 눌러 주세요.";
            var s = _steps.FirstOrDefault(x => x.Id == step);
            if (s is null || s.Actions.All(a => a.Id != action)) return "지금은 그 버튼을 쓸 수 없습니다.";
        }
        switch (step, action)
        {
            case ("polar", "pa-done"):
                lock (_gate) _paConfirmed = true;
                PolarWaiting();
                break;
            case ("polar", "reopen"): Run(OpenToolAsync); break;
            case ("polar", "retry"): Run(PolarAsync); break;
            case ("reconnect", "retry"): Run(ReconnectAsync); break;
            case ("move", "recheck"): MoveWaiting(); break;
            case ("move", "move" or "retry"): Run(MoveAsync); break;
            case ("focus", "retry"): Run(FocusAsync); break;
            case ("focus", "manual"):
                Set("focus", PrepStatus.Waiting, "초점을 손으로 맞춘 뒤 눌러 주세요.", [new("focus-done", "초점을 맞췄어요", true)]);
                break;
            case ("focus", "focus-done"):
                Set("focus", PrepStatus.Pass, "직접 맞췄습니다 (사용자 확인).");
                Run(CenterAsync);
                break;
            case ("center", "retry"): Run(CenterAsync); break;
            case ("center", "refocus"): Run(FocusAsync); break;
            case ("guiding", "retry"): Run(GuidingAsync); break;
            case ("guiding", "remeasure"): Run(RemeasureAsync); break;
            case ("guiding", "ok"):
                lock (_gate)
                {
                    var g = S("guiding");
                    g.Status = PrepStatus.Pass;
                    g.Actions = [];
                    Changed();
                }
                Run(TestAsync);
                break;
            case ("test", "retake" or "retry"): Run(TestAsync); break;
            case ("test", "recenter"): Run(CenterAsync); break;
            case ("test", "start"):
                lock (_gate)
                {
                    var t = S("test");
                    t.Status = PrepStatus.Pass;
                    t.Message = "촬영을 시작합니다.";
                    t.Actions = [];
                    _ready = true;
                    Changed();
                }
                log.LogInformation("준비 완료: 촬영 시작");
                break;
        }
        return null;
    }

    // ── ① 극축정렬 ─────────

    private async Task PolarAsync(CancellationToken ct)
    {
        if (_guiding)
        {
            Set("polar", PrepStatus.Running, "PHD2가 가이드 카메라를 놓게 하는 중입니다.");
            var released = await devices.ReleaseGuideCameraAsync(ct);
            if (!released.Ok)
            {
                Set("polar", PrepStatus.Fail, released.Problem ?? "PHD2가 가이드 카메라를 놓지 않았습니다.", [Retry]);
                return;
            }
        }
        await OpenToolAsync(ct);
    }

    private async Task OpenToolAsync(CancellationToken ct)
    {
        Set("polar", PrepStatus.Running, "SharpCap을 여는 중입니다.");
        lock (_gate) _toolExited = false;
        var opened = await devices.OpenPolarAlignToolAsync(OnToolExited, ct);
        lock (_gate) _toolTracked = opened.Ok;
        PolarWaiting();
    }

    private void OnToolExited()
    {
        lock (_gate) _toolExited = true;
        PolarWaiting();
    }

    /// <summary>극축정렬 칸의 문장·버튼을 두 조건(사용자 확인, SharpCap 종료)에 맞춘다. 둘 다면 다음 칸으로</summary>
    private void PolarWaiting()
    {
        bool confirmed, exited, tracked;
        lock (_gate)
        {
            if (_plan is null || S("polar").Status is PrepStatus.Pass) return;
            confirmed = _paConfirmed;
            exited = _toolExited;
            tracked = _toolTracked;
        }
        var done = new PrepAction("pa-done", "극축정렬을 마쳤어요", true);
        if (confirmed && (exited || !tracked))
        {
            Set("polar", PrepStatus.Pass, "극축정렬을 마쳤습니다 (사용자 확인).");
            if (_guiding) Run(ReconnectAsync);
            else MoveWaiting();
        }
        else if (confirmed)
            Set("polar", PrepStatus.Waiting, "SharpCap을 종료해 주세요. 종료되면 바로 이어집니다.");
        else if (exited)
            Set("polar", PrepStatus.Waiting, "SharpCap이 종료됐습니다. 극축정렬을 마치셨나요?", [done, new("reopen", "SharpCap 다시 열기")]);
        else if (tracked)
            Set("polar", PrepStatus.Waiting, "SharpCap에서 극축정렬을 진행해 주세요. 필요하면 가이드 카메라 초점을 맞추고, 끝나면 SharpCap을 종료해 주세요.", [done]);
        else
            Set("polar", PrepStatus.Waiting, "SharpCap을 열지 못했습니다. SharpCap을 직접 열어 극축정렬을 진행해 주세요.", [done]);
    }

    // ── ② 가이드 연결 ─────────

    private const int ReconnectTries = 3;
    private static readonly TimeSpan ReconnectGap = TimeSpan.FromSeconds(3); // SharpCap 종료 직후 드라이버가 늦게 풀릴 수 있다

    private async Task ReconnectAsync(CancellationToken ct)
    {
        DeviceResult result = DeviceResult.Success;
        for (var i = 1; i <= ReconnectTries; i++)
        {
            Set("reconnect", PrepStatus.Running, i == 1
                ? "가이드 카메라를 PHD2에 다시 연결하고 있습니다."
                : $"가이드 카메라를 PHD2에 다시 연결하고 있습니다 ({i}/{ReconnectTries}).");
            result = await devices.ReconnectGuideCameraAsync(ct);
            if (result.Ok) break;
            if (i < ReconnectTries) await Task.Delay(ReconnectGap, ct);
        }
        if (!result.Ok)
        {
            Set("reconnect", PrepStatus.Fail, $"가이드 카메라를 다시 연결하지 못했습니다. {result.Problem}".Trim(), [Retry]);
            return;
        }
        Set("reconnect", PrepStatus.Pass, "가이드 카메라가 연결되었습니다.");
        MoveWaiting();
    }

    // ── ③ 대상 이동 (승인 한 번으로 ③~⑤) ─────────

    /// <summary>대상이 지금 30° 위인지 보고, 위면 "대상으로 이동" 버튼. 아니면 알리고 멈춤 (2026-10-01 결정)</summary>
    private void MoveWaiting()
    {
        PreparationPlan plan;
        NightContext night;
        bool ignore;
        lock (_gate)
        {
            if (_plan is null || _night is null) return;
            plan = _plan;
            night = _night;
            ignore = _ignoreAltitude;
        }
        var name = TargetName(plan);
        var now = DateTimeOffset.Now;
        var alt = Astronomy.Altitude(new Equatorial(plan.RaDegrees, plan.DecDegrees), night.Site, now.UtcDateTime);
        if (alt < UsableAltitude && !ignore)
        {
            var when = now < plan.Start
                ? $"{plan.Start:HH:mm}쯤 30° 위로 올라옵니다. 그때 다시 확인해 주세요."
                : "오늘 밤 촬영할 수 있는 시간이 지났습니다. 계획으로 돌아가 다른 대상을 골라 주세요.";
            Set("move", PrepStatus.Waiting, $"{name}{Josa.Pick(name, "은", "는")} 지금 고도 {alt:F0}°로 아직 보이지 않습니다. {when}",
                [new("recheck", "다시 확인")], new { altitude = Math.Round(alt) });
            return;
        }
        Set("move", PrepStatus.Waiting, $"준비되면 {name}{Ro(name)} 이동합니다. 초점·센터링까지 이어서 진행합니다.",
            [new("move", "대상으로 이동", true)], new { altitude = Math.Round(alt) });
    }

    private async Task MoveAsync(CancellationToken ct)
    {
        var plan = _plan!;
        var name = TargetName(plan);
        Set("move", PrepStatus.Running, $"{name}{Ro(name)} 이동 중입니다.");
        var moved = await devices.SlewAsync(plan.RaDegrees, plan.DecDegrees, ct);
        if (!moved.Ok)
        {
            Set("move", PrepStatus.Fail, moved.Problem ?? "적도의가 이동하지 못했습니다.", [Retry]);
            return;
        }
        Set("move", PrepStatus.Pass, "이동했습니다.");
        await FocusAsync(ct);
    }

    // ── ④ 초점 ─────────

    private async Task FocusAsync(CancellationToken ct)
    {
        if (equipment.IsWithout("focuser") || !await devices.HasFocuserAsync(ct))
        {
            lock (_gate) S("focus").Term = "직접";
            Set("focus", PrepStatus.Waiting, "자동초점 장치가 없습니다. 초점을 손으로 맞춘 뒤 눌러 주세요.", [new("focus-done", "초점을 맞췄어요", true)]);
            return;
        }
        Set("focus", PrepStatus.Running, "자동초점 중입니다.");
        var focus = await devices.AutofocusAsync(ct);
        if (!focus.Ok)
        {
            Set("focus", PrepStatus.Fail, focus.Problem ?? "자동초점에 실패했습니다.", [Retry, new("manual", "손으로 맞췄어요")]);
            return;
        }
        Set("focus", PrepStatus.Pass, focus.Hfr is { } h ? $"초점을 맞췄습니다 (별 크기 HFR {h:F1})." : "초점을 맞췄습니다.", detail: new { hfr = focus.Hfr });
        await CenterAsync(ct);
    }

    // ── ⑤ 센터링 ─────────

    private async Task CenterAsync(CancellationToken ct)
    {
        var plan = _plan!;
        Set("center", PrepStatus.Running, "사진을 찍어 위치를 계산하고 가운데로 맞추는 중입니다.");
        var c = await devices.CenterAsync(plan.RaDegrees, plan.DecDegrees, ct);
        if (!c.Ok)
        {
            Set("center", PrepStatus.Fail, c.Problem ?? "센터링에 실패했습니다.", [Retry, new("refocus", "초점 다시 맞추기")]);
            return;
        }
        var parts = new List<string>();
        if (c.ErrorArcmin is { } e) parts.Add($"오차 {e:F1}′");
        if (c.RotationDegrees is { } r) parts.Add($"카메라 방향 {r:F0}°");
        Set("center", PrepStatus.Pass, parts.Count > 0 ? $"가운데로 맞췄습니다 ({string.Join(" · ", parts)})." : "가운데로 맞췄습니다.",
            detail: new { errorArcmin = c.ErrorArcmin, rotation = c.RotationDegrees });
        // 적도의가 움직였으니 가이딩은 다시 (시험 사진에서 "다시 센터링"으로 왔을 때도)
        if (_guiding) await GuidingAsync(ct);
        else await TestAsync(ct);
    }

    // ── ⑥ 가이딩 ─────────

    private async Task GuidingAsync(CancellationToken ct)
    {
        var evening = TonightService.EveningOf(DateTimeOffset.Now);
        bool calibrate;
        lock (_gate) calibrate = _calibratedEvening != evening;
        var result = await devices.StartGuidingAndMeasureAsync(calibrate, phase => Set("guiding", PrepStatus.Running, phase switch
        {
            GuidingPhase.SelectingStar => "가이드 별을 고르는 중입니다.",
            GuidingPhase.Calibrating => "캘리브레이션 중입니다 (1~2분).",
            GuidingPhase.Settling => "가이딩이 안정되기를 기다리는 중입니다.",
            _ => "가이딩 오차를 재는 중입니다.",
        }), ct);
        if (result.Ok && calibrate) lock (_gate) _calibratedEvening = evening;
        GuidingVerdict(result);
    }

    private async Task RemeasureAsync(CancellationToken ct)
    {
        Set("guiding", PrepStatus.Running, "가이딩 오차를 다시 재는 중입니다.");
        GuidingVerdict(await devices.MeasureGuidingAsync(ct));
    }

    /// <summary>
    /// 가이딩 판정 (2026-10-01 결정): 기준 = 주 카메라 한 픽셀이 담는 하늘 크기. 전체 오차가 기준 이하·별 잃음 없음 = 충분,
    /// 1.5배 이하 = 지켜보기, 그 위 = 문제. 측정값이 없으면 통과시키지 않는다. 확정은 사용자가 "좋아요"로.
    /// (인터넷이 되면 앱의 AI가 같은 수치로 설명을 덧붙인다 — W6)
    /// </summary>
    private void GuidingVerdict(GuidingResult result)
    {
        if (!result.Ok || result.Stats is not { Samples: > 0 } st)
        {
            Set("guiding", PrepStatus.Fail, result.Problem ?? "가이딩 측정값이 없습니다. 다시 시도해 주세요.", [Retry]);
            return;
        }
        var limit = _night?.Rig.PixelScale is > 0 and var px ? px : 2.0;
        var ratio = st.TotalArcsec / limit;
        var (verdict, word) = ratio <= 1 && st.StarLost == 0 ? ("good", "충분해요")
            : ratio <= 1.5 ? ("watch", "조금 더 지켜보세요")
            : ("bad", "문제가 있어요");
        var lost = st.StarLost > 0 ? $", 별을 {st.StarLost}번 잃음" : "";
        Set("guiding", PrepStatus.Waiting, $"{word}. 가이딩 오차 {st.TotalArcsec:F1}″ (기준 {limit:F1}″ = 주 카메라 한 픽셀{lost})",
            [new("ok", "좋아요", verdict != "bad"), new("remeasure", "다시 재기", verdict == "bad")],
            new { verdict, total = st.TotalArcsec, ra = st.RaArcsec, dec = st.DecArcsec, limit = Math.Round(limit, 2), samples = st.Samples, starLost = st.StarLost });
    }

    // ── ⑦ 시험 사진 (항상 찍는다, 2026-10-01 결정) ─────────

    private async Task TestAsync(CancellationToken ct)
    {
        var plan = _plan!;
        Set("test", PrepStatus.Running, $"시험 사진을 찍는 중입니다 ({plan.ExposureSeconds}초).");
        var shot = await devices.TestShotAsync(plan.ExposureSeconds, plan.Iso, ct);
        if (!shot.Ok)
        {
            Set("test", PrepStatus.Fail, shot.Problem ?? "시험 사진을 찍지 못했습니다.", [Retry]);
            return;
        }
        var info = new List<string>();
        if (shot.Stars is { } n) info.Add($"별 {n}개");
        if (shot.Hfr is { } h) info.Add($"별 크기 HFR {h:F1}");
        Set("test", PrepStatus.Waiting, info.Count > 0 ? $"시험 사진을 확인해 주세요. {string.Join(" · ", info)}" : "시험 사진을 확인해 주세요.",
            [new("start", "촬영 시작", true), new("retake", "다시 찍기"), new("recenter", "다시 센터링")],
            new { imageUrl = shot.ImageUrl, stars = shot.Stars, hfr = shot.Hfr });
    }

    // ── [임시] 화면 확인용 ─────────

    /// <summary>[임시] 낮에 흐름을 볼 때: 대상이 보인다고 가정하고 이동 칸을 다시 확인</summary>
    public void IgnoreAltitude()
    {
        lock (_gate) _ignoreAltitude = true;
        if (View().Steps.FirstOrDefault(s => s.Id == "move") is { Status: PrepStatus.Waiting }) MoveWaiting();
    }

    // ── 도우미 ─────────

    private static string TargetName(PreparationPlan p) => p.TargetKoreanName is { } k ? $"{p.TargetName} {k}" : p.TargetName;

    /// <summary>"으로/로": 받침이 없거나 ㄹ 받침이면 "로" (M31 → 일 → "로")</summary>
    private static string Ro(string word)
    {
        var s = word.TrimEnd();
        if (s.Length == 0) return "로";
        var c = s[^1];
        if (c is >= '가' and <= '힣') { var jong = (c - '가') % 28; return jong is 0 or 8 ? "로" : "으로"; }
        if (char.IsAsciiDigit(c)) return "1278".Contains(c) ? "로" : Josa.Pick(word, "으로", "로");
        if (char.IsAsciiLetter(c)) return "LlRr".Contains(c) ? "로" : Josa.Pick(word, "으로", "로"); // 엘·알은 ㄹ 받침
        return "로";
    }
}
