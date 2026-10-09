using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Focus;

/// <summary>장비 준비 ③ 초점 장비 (대상 묶음의 초점 확인도 같은 장비). 실제: N.I.N.A. 포커서 이동·자동초점(설정은 AA 추천값으로 먼저), 온도 프로브</summary>
public interface IFocusDevices
{
    /// <summary>포커서 범위(걸음). 제조사 설정의 0~최대 (DESIGN.md ④ — 한계는 제조사 기능에 맡김)</summary>
    Task<(int Min, int Max)> LimitsAsync(CancellationToken ct);
    Task<int> PositionAsync(CancellationToken ct);
    /// <summary>포커서가 연결되어 있는가 (모의는 항상)</summary>
    Task<bool> ConnectedAsync(CancellationToken ct) => Task.FromResult(true);
    /// <summary>바깥 기온(°C). 프로브가 없으면 null</summary>
    Task<double?> TemperatureAsync(CancellationToken ct);
    Task<FocuserMove> MoveAsync(int position, CancellationToken ct);
    /// <summary>자동초점. 지점마다 알린다(위치, HFR). 별이 없으면 StarsFound=false</summary>
    Task<AutofocusRun> AutofocusAsync(Action<int, double> point, CancellationToken ct);
    /// <summary>범위 안에서 넓은 간격으로 훑어 별이 보이는 위치를 찾는다. 못 찾으면 null</summary>
    Task<int?> CoarseSearchAsync(int min, int max, int from, Action<int> visiting, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<FocusEndState> ReadEndStateAsync(CancellationToken ct);

    /// <summary>
    /// 출발 전 점검의 "포커서 0점"을 확인했으면 지금 위치를 0으로 정한다 (2026-10-09 사용자 결정 — 포커서를 다시 달면 기어가 맞물리며 노브가 조금 돌아감).
    /// 요청이 없으면 None. 모의 장비는 하지 않는다
    /// </summary>
    Task<FocuserZero> ZeroIfRequestedAsync(CancellationToken ct) => Task.FromResult(FocuserZero.None);
    bool ZeroRequested => false;
    /// <summary>
    /// 자동초점이 시작점에서 안팎으로 훑는 거리(걸음). 시작점이 포커서 끝에서 이보다 가까우면 범위 절반이 기계 밖으로 나가 끝에서 멈춘다
    /// (2026-10-09 실기: 0에서 시작 → 500·375·250·125·0 뒤 더 안쪽으로 못 가 0에서 움찔거림). 모르면 0
    /// </summary>
    Task<int> AutofocusReachAsync(CancellationToken ct) => Task.FromResult(0);
    /// <summary>
    /// 이 망원경에서 지난번 맞은 초점 위치 (밤을 넘어 저장). 포커서를 달 때 매번 끝까지 넣은 위치를 0으로 잡으므로(포커서 0점) 숫자의 뜻이 밤마다 같다
    /// </summary>
    (int Position, double? TemperatureC)? SavedFocus => null;
    void SaveFocus(int position, double? temperatureC) { }
    /// <summary>0점 요청을 거둔다 (사용자가 직접 했거나 건너뜀)</summary>
    void DropZeroRequest() { }
}

/// <summary>0점 잡기 결과. Unsupported = AA가 0점을 잡을 수 없는 포커서 (드라이버 설정 창에서 직접)</summary>
public sealed record FocuserZero(bool Requested, bool Done, bool Unsupported = false, string? Problem = null)
{
    public static readonly FocuserZero None = new(false, false);
}

/// <summary>출발 전 점검에서 "포커서 0점"을 확인했다는 표시 — 다음 초점 작업이 0점을 잡는다. 이어서 하기는 점검을 건너뛰므로 잡지 않는다</summary>
public sealed class FocuserZeroRequest
{
    public volatile bool Pending;
}

/// <summary>포커서 이동 결과. Stalled = 제조사 멈춤 감지(끝에 닿음)</summary>
public sealed record FocuserMove(bool Ok, bool Stalled = false, string? Problem = null);
/// <summary>StopUnconfirmed = 자동초점을 취소했지만 멈춘 것(카메라·포커서 쉼)을 확인하지 못함 — 이 상태로 다시 찍거나 자동초점을 하지 않는다 (CX-NIGHT-06)</summary>
public sealed record AutofocusRun(bool Ok, bool StarsFound, int BestPosition, double Hfr, bool CurveGood, bool Stalled = false, string? Problem = null, bool StopUnconfirmed = false);
public sealed record FocusEndState(bool Moving, int Position, bool Error);

/// <summary>
/// 장비 준비 ③ 초점 (DESIGN.md ④ — 캘리브레이션 위치의 별로, 대상과 무관): 지난번 맞은 위치(그때 기온과 함께)에서 시작 → 자동초점(AA 추천값) → 결과를 AA 말로.
/// 별이 안 보이면 포커서 범위 안에서 넓게 훑기 → 그래도 안 되면 손 초점 안내. 포커서 에러(멈춤 감지)는 바로 멈추고 해제 안내.
/// </summary>
public sealed class FocusTask(IFocusDevices devices) : IPrepTask
{
    private int _best;

    public string Id => "focus";
    public string Title => "초점";
    public string StartLabel => "초점 맞추기";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("start", "시작 위치로"), new("measure", "별 크기 측정"), new("apply", "최적 위치 적용")];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        if (!ctx.HasFocuser) return await ManualAsync(run, "자동초점 장치가 없습니다. 초점을 손으로 맞춘 뒤 눌러 주세요.", ct);
        run.Live("focus-curve", LiveUrl, new { points = Array.Empty<object>(), last = ctx.Memory.LastFocus?.Position });
        if (devices.ZeroRequested) await ZeroAsync(run, ct);

        while (true)
        {
            // 시작 위치: 지난번 맞은 위치 (없으면 지금 위치)
            run.SubStep(0);
            run.Guide("시작 위치로", "포커서를 지난번에 맞았던 위치로 옮긴 뒤, 조금씩 옮기며 별 크기를 잽니다.");
            var temp = await devices.TemperatureAsync(ct);
            // 같은 밤에 맞춘 위치 → 저장해 둔 지난 밤 위치 → 지금 위치
            ctx.Memory.LastFocus ??= devices.SavedFocus;
            var start = ctx.Memory.LastFocus?.Position ?? await devices.PositionAsync(ct);
            run.Status(ctx.Memory.LastFocus is { } lf ? $"지난번 위치 {lf.Position:N0}에서 시작합니다{TempText(temp, lf.TemperatureC)}" : "지금 위치에서 시작합니다");
            // 자동초점 범위가 포커서 0~최대 안에 들도록 시작점을 끝에서 떼어 놓는다
            var reach = await devices.AutofocusReachAsync(ct);
            if (reach > 0)
            {
                var (lo, hi) = await devices.LimitsAsync(ct);
                var safe = hi - lo > 2 * reach ? Math.Clamp(start, lo + reach, hi - reach) : start;
                if (safe != start)
                {
                    run.Status($"자동초점은 {reach:N0}걸음 안팎을 재므로 포커서 끝에서 떨어진 {safe:N0}에서 시작합니다");
                    start = safe;
                }
            }
            var moved = await devices.MoveAsync(start, ct);
            if (!moved.Ok) { if (await FocuserErrorAsync(run, moved.Stalled, moved.Problem, ct)) continue; return ManualDone(ctx); }

            run.SubStep(1);
            run.Guide("별 크기 측정", "포커서를 조금씩 옮기며 별 크기를 잽니다. 가장 작아지는 위치를 찾습니다.");
            // 측정점이 오기 전까지도 지금 하는 일을 보인다 (2026-10-08 실기: "지금 위치에서 시작합니다"에 멈춘 것처럼 보임)
            run.Status("자동초점 중입니다 · N.I.N.A.가 포커서를 옮기며 별 크기를 재고 있어요 (보통 몇 분)");
            var points = new List<object>();
            var af = await devices.AutofocusAsync((pos, hfr) =>
            {
                points.Add(new { pos, hfr });
                run.Readout("hfr", $"{hfr:F1}", "별 크기 (HFR) · 이번 지점", Tone.Busy, new Dictionary<string, double> { ["hfr"] = hfr, ["position"] = pos }, live: true);
                run.Live("focus-curve", LiveUrl, new { points = points.ToArray(), last = ctx.Memory.LastFocus?.Position });
                run.Status($"자동초점 중입니다 · {points.Count} / 9 지점 · AA 추천값");
            }, ct);
            // 자동초점이 멈췄는지 모르면 다시 자동초점·사진을 하지 않고 멈춤 확인부터 (CX-NIGHT-06)
            while (af.StopUnconfirmed)
            {
                run.Status($"{af.Problem}. N.I.N.A.에서 자동초점이 멈췄는지 확인한 뒤 눌러 주세요", Tone.Fail);
                await run.AskAsync([new("recheck-stop", "장비 상태 다시 확인", true)], ct);
                if (await devices.StopAsync(ct)) af = af with { StopUnconfirmed = false };
            }
            if (af.Stalled) { if (await FocuserErrorAsync(run, true, af.Problem, ct)) continue; return ManualDone(ctx); }
            if (!af.StarsFound)
            {
                // 별이 안 보임 → 범위 안에서 넓게 훑기
                var (min, max) = await devices.LimitsAsync(ct);
                run.Status("별이 보이지 않아 넓은 간격으로 크게 훑는 중입니다", Tone.Warn);
                var found = await devices.CoarseSearchAsync(min, max, start, p =>
                    run.Readout("search", $"{p:N0}", "넓게 훑는 중 · 찾은 별 0개", Tone.Warn, new Dictionary<string, double> { ["position"] = p }), ct);
                if (found is { } f)
                {
                    ctx.Memory.LastFocus = (f, temp);
                    continue; // 찾은 곳에서 다시 자동초점
                }
                run.Guide("초점을 대략 손으로 맞춰 주세요", "넓게 훑어도 별을 찾지 못했습니다. 밝은 별이 점으로 보일 때까지 포커서를 손으로 돌린 뒤 다시 자동초점을 누르세요. 덮개가 닫혀 있지 않은지도 확인해 주세요.");
                run.Status("자동초점에서 별을 찾지 못했습니다", Tone.Fail);
                var c = await run.AskAsync([new("retry", "다시 자동초점", true), new("manual", "지금 초점으로 진행")], ct);
                if (c == "manual") return ManualDone(ctx);
                continue;
            }
            if (!af.Ok || !af.CurveGood)
            {
                run.Guide("다시 하는 게 좋아요", "곡선이 고르지 않습니다. 구름이 지나가거나 바람에 흔들렸을 수 있습니다.");
                run.Status(af.Problem ?? "초점 곡선이 고르지 않습니다", Tone.Warn);
                var c = await run.AskAsync([new("retry", "다시 자동초점", true), new("manual", "지금 초점으로 진행")], ct);
                if (c == "manual") return ManualDone(ctx);
                continue;
            }

            run.SubStep(2);
            _best = af.BestPosition;
            ctx.Memory.LastFocus = (af.BestPosition, temp);
            devices.SaveFocus(af.BestPosition, temp); // 다음 밤 시작 위치
            var tempText = temp is { } t ? $" · 기온 {t:F1}°C 기억" : "";
            run.Readout("hfr", $"{af.Hfr:F1}", $"별 크기 (HFR) · 가장 좋은 위치 {af.BestPosition:N0}{tempText}", Tone.Ok,
                new Dictionary<string, double> { ["hfr"] = af.Hfr, ["position"] = af.BestPosition });
            run.Status(null);
            return new Completed(new FocusResult(false, af.BestPosition, af.Hfr, temp, ctx.Now()), $"좋아요 · HFR {af.Hfr:F1} · 위치 {af.BestPosition:N0}",
                "좋아요", "곡선이 깔끔해요. 가장 좋은 위치를 이번 기온과 함께 기억했어요. 이것으로 장비 준비가 끝났고, 오늘 밤 대상을 바꿔도 그대로 씁니다.",
                [new("redo:focus", "다시 자동초점")]);
        }
    }

    /// <summary>포커서 0점 (점검에서 확인한 경우만). 잡으면 지난 초점 위치 기억은 기준이 달라져 버린다</summary>
    private async Task ZeroAsync(ITaskRun run, CancellationToken ct)
    {
        while (true)
        {
            run.SubStep(0);
            run.Status("포커서 0점을 잡는 중입니다 · N.I.N.A. 포커서 연결을 잠시 끊습니다");
            var z = await devices.ZeroIfRequestedAsync(ct);
            if (!z.Requested) { run.Status(null); return; }
            run.Context.Memory.LastFocus = null;
            if (z.Done) { run.Status("포커서 0점을 잡았습니다", Tone.Ok); return; }
            if (z.Unsupported)
            {
                run.Guide("포커서 0점", "이 포커서는 AA가 0점을 잡을 수 없습니다. 포커서 드라이버 설정 창(N.I.N.A. 포커서 톱니바퀴)에서 Set Zero를 누른 뒤 눌러 주세요.");
                run.Status(null);
                await run.AskAsync([new("zero-done", "0점을 잡았어요", true), new("zero-skip", "0점 없이 진행")], ct);
                devices.DropZeroRequest();
                return;
            }
            run.Guide("포커서 0점을 잡지 못했습니다", "다시 시도하거나, Oasis 설정 창(N.I.N.A. 포커서 톱니바퀴)에서 Set Zero를 직접 누른 뒤 진행해 주세요.");
            run.Status(z.Problem ?? "포커서 0점을 잡지 못했습니다", Tone.Fail);
            var c = await run.AskAsync([new("retry", "다시 시도", true), new("zero-done", "직접 0점을 잡았어요"), new("zero-skip", "0점 없이 진행")], ct);
            if (c == "retry") continue;
            devices.DropZeroRequest();
            return;
        }
    }

    /// <summary>하늘 화면 바탕: N.I.N.A.가 찍은 마지막 사진 (실장비만 — 모의는 그림으로). N.I.N.A. 자동초점 창 대신</summary>
    private const string LiveUrl = "/api/prepare/live/nina";

    private static string TempText(double? now, double? then) =>
        now is { } n && then is { } t ? $" (그때 {t:F1}°C, 지금 {n:F1}°C)" : "";

    /// <summary>포커서 에러: 멈춤 감지면 해제 안내. 다시 시도를 누르면 true, "지금 초점으로 진행"이면 false (2026-10-08 — 포커서 문제로 밤이 막히지 않게)</summary>
    private static async Task<bool> FocuserErrorAsync(ITaskRun run, bool stalled, string? problem, CancellationToken ct)
    {
        run.Guide("포커서가 멈췄습니다", stalled
            ? "포커서가 끝에 닿아 멈춘 것 같습니다. 포커서 설정 창(N.I.N.A. 포커서 톱니바퀴)에서 Clear stall을 누른 뒤 다시 시도해 주세요."
            : "포커서가 응답하지 않습니다. 연결을 확인한 뒤 다시 시도해 주세요.");
        run.Status(problem ?? (stalled ? "포커서 멈춤 감지" : "포커서 오류"), Tone.Fail);
        return await run.AskAsync([new("retry", "다시 시도", true), new("manual", "지금 초점으로 진행")], ct) == "retry";
    }

    private static async Task<TaskOutcome> ManualAsync(ITaskRun run, string text, CancellationToken ct)
    {
        run.Guide("초점을 손으로 맞춰 주세요", text);
        run.Status(null);
        await run.AskAsync([new("manual-done", "초점을 맞췄어요", true)], ct);
        return ManualDone(run.Context);
    }

    private static Completed ManualDone(PrepContext ctx) =>
        new(new FocusResult(true, null, null, null, ctx.Now()), "손으로 맞춤", "손으로 맞췄어요", "이것으로 장비 준비가 끝났습니다. 사진이 흐리면 대상 단계의 센터링이 안 될 수 있어요.");

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        if (!ctx.HasFocuser || ctx.Results.Get<FocusResult>() is { Manual: true }) return EndStateCheck.Pass;
        var s = await devices.ReadEndStateAsync(ct);
        if (s.Error) return EndStateCheck.Fail("포커서가 오류(멈춤 감지) 상태입니다");
        if (s.Moving) return EndStateCheck.Fail("포커서가 아직 움직이고 있습니다");
        if (s.Position != _best) return EndStateCheck.Fail($"포커서가 가장 좋은 위치({_best:N0})에 있지 않습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}
