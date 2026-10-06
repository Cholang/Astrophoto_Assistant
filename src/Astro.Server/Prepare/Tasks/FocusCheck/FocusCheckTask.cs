using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Tasks.Focus;

namespace Astro.Server.Prepare.Tasks.FocusCheck;

/// <summary>
/// 대상 묶음의 초점 확인 (DESIGN.md "단계 재구성" — 2026-10-07). 장비 준비 때 맞춘 초점을 그대로 써도 되는지 근거 두 가지로 본다:
/// ① 초점 맞춘 뒤 기온 변화 2°C 이상, ② 센터링 사진의 별 크기가 초점 때보다 30% 이상 큼 (센터링 사진은 짧은 노출·비닝이라 기준을 넉넉히,
/// 별이 충분히 잡혔을 때만). 둘 다 괜찮으면 근거를 잠깐 보여 주고 묻지 않고 넘어가고, 하나라도 걸리면 "다시 맞출까요?".
/// 센터링 실패·시험 사진이 "초점 다시 맞추기"를 고르면(ctx.RefocusRequested) 비교 없이 바로 다시 맞춘다.
/// 장비(포커서)는 장비 준비의 초점과 같은 것을 쓴다.
/// </summary>
public sealed class FocusCheckTask(IFocusDevices devices) : IPrepTask
{
    public const double TemperatureLimitC = 2;
    public const double HfrGrowthLimit = 1.3;
    public const int MinStars = 10;

    private int? _best;

    public string Id => "focuscheck";
    public string Title => "초점 확인";
    /// <summary>앞 작업(센터링)의 끝 버튼 — 센터링 사진을 보고 이 대상으로 확정</summary>
    public string StartLabel => "이 대상으로 확정";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("compare", "초점 때와 비교"), new("refocus", "다시 맞추기")];

    /// <summary>포커서가 있고 장비 준비에서 자동초점으로 맞췄을 때 (손 초점이면 비교할 기준이 없다)</summary>
    public bool AppliesTo(PrepContext ctx) => ctx.HasFocuser && ctx.Results.Get<FocusResult>() is not { Manual: true };

    public sealed record Evidence(double? TemperatureNowC, double? TemperatureChangeC, double? CenterHfr, double? FocusHfr, int? Stars)
    {
        public bool TemperatureBad => TemperatureChangeC is { } d && Math.Abs(d) >= TemperatureLimitC;
        /// <summary>별이 충분히 잡혔을 때만 비교한다</summary>
        public bool HfrComparable => CenterHfr is > 0 && FocusHfr is > 0 && Stars >= MinStars;
        public bool HfrBad => HfrComparable && CenterHfr > FocusHfr * HfrGrowthLimit;
        public int? GrowthPercent => HfrComparable ? (int)Math.Round((CenterHfr!.Value / FocusHfr!.Value - 1) * 100) : null;
    }

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        _best = null;
        var focus = ctx.Results.Get<FocusResult>();
        var center = ctx.Results.Get<CenterResult>();
        run.SubStep(0);
        run.Guide("초점이 아직 맞는지 보고 있습니다", "장비 준비 때 맞춘 초점을 그대로 써도 되는지, 기온 변화와 방금 센터링 사진의 별 크기로 봅니다.");
        run.Status("초점 때와 비교하는 중입니다");
        var temp = await devices.TemperatureAsync(ct);
        var ev = new Evidence(temp, temp is { } t && focus?.TemperatureC is { } ft ? Math.Round(t - ft, 1) : null, center?.Hfr, focus?.Hfr, center?.Stars);
        Show(run, ev);

        if (ctx.RefocusRequested)
        {
            ctx.RefocusRequested = false;
            run.Guide("초점을 다시 맞춥니다", "초점이 밀렸을 수 있다고 하셔서 대상 근처 별로 자동초점을 다시 합니다.");
            return await RefocusAsync(run, ev, ct);
        }

        if (!ev.TemperatureBad && !ev.HfrBad)
        {
            run.Status("초점 유지", Tone.Ok);
            return new Completed(new FocusCheckResult(false, ev.TemperatureChangeC, ev.CenterHfr, null, null, null, ctx.Now()), "그대로 씀",
                "그대로 써도 돼요", "기온도 별 크기도 초점 맞출 때와 비슷해요. 다시 맞추지 않고 가이딩으로 넘어갑니다. 시험 사진에서 한 번 더 확인해요.")
            { AutoNext = true };
        }

        var why = ev.TemperatureBad && ev.HfrBad
            ? $"초점을 맞춘 뒤 기온이 {Math.Abs(ev.TemperatureChangeC!.Value):F1}°C {(ev.TemperatureChangeC < 0 ? "내려갔고" : "올라갔고")} 센터링 사진의 별도 {ev.GrowthPercent}% 커졌어요."
            : ev.TemperatureBad
                ? $"초점을 맞춘 뒤 기온이 {Math.Abs(ev.TemperatureChangeC!.Value):F1}°C {(ev.TemperatureChangeC < 0 ? "내려갔어요. 경통이 줄어" : "올라갔어요. 경통이 늘어")} 초점이 밀렸을 수 있어요."
                : $"기온은 거의 그대로인데 센터링 사진의 별이 {ev.GrowthPercent}% 커졌어요. 포커서가 밀렸거나 장비를 건드렸을 수 있어요.";
        run.Guide("초점을 다시 맞출까요?", why + " 자동초점은 2분쯤 걸려요.");
        run.Status(ev.TemperatureBad ? "기온 변화가 커요" : "별 크기가 커졌어요", Tone.Warn);
        var c = await run.AskAsync([new("refocus", "다시 맞추기", true), new("keep", "그대로 진행")], ct);
        if (c == "keep")
            return new Completed(new FocusCheckResult(false, ev.TemperatureChangeC, ev.CenterHfr, null, null, null, ctx.Now()), "그대로 진행",
                "그대로 진행합니다", "초점을 다시 맞추지 않고 가이딩으로 넘어갑니다. 시험 사진에서 별 크기를 한 번 더 확인해요.") { AutoNext = true };
        return await RefocusAsync(run, ev, ct);
    }

    private static void Show(ITaskRun run, Evidence ev)
    {
        var values = new Dictionary<string, double>();
        if (ev.TemperatureChangeC is { } d) values["temperatureChange"] = d;
        if (ev.TemperatureNowC is { } t) values["temperature"] = t;
        if (ev.CenterHfr is { } h) values["centerHfr"] = h;
        if (ev.FocusHfr is { } f) values["focusHfr"] = f;
        if (ev.Stars is { } s) values["stars"] = s;
        var tempLine = ev.TemperatureChangeC is { } dc ? $"기온 변화 {Math.Abs(dc):F1}°C (기준 {TemperatureLimitC:0}°C)" : "기온을 모름";
        var hfrLine = ev.HfrComparable ? $"센터링 사진 별 크기 {ev.CenterHfr:F1} · 초점 때 {ev.FocusHfr:F1} ({ev.GrowthPercent:+0;-0}%)" : "센터링 사진 별 크기는 비교하지 않음 (별이 적음)";
        var tone = ev.TemperatureBad || ev.HfrBad ? Tone.Warn : Tone.Ok;
        run.Readout("focus-check", ev.TemperatureChangeC is { } dd ? $"{Math.Abs(dd):F1}°C" : ev.CenterHfr is { } ch ? $"{ch:F1}" : "—",
            $"{tempLine} · {hfrLine}", tone, values);
    }

    private async Task<TaskOutcome> RefocusAsync(ITaskRun run, Evidence ev, CancellationToken ct)
    {
        var ctx = run.Context;
        while (true)
        {
            run.SubStep(1);
            run.Guide("초점을 다시 맞추고 있습니다", "대상 근처 별로 자동초점을 합니다. 지난번 맞은 위치에서 시작합니다.");
            run.Status("시작 위치로 옮기는 중입니다");
            if (ctx.Memory.LastFocus is { } lf)
            {
                var moved = await devices.MoveAsync(lf.Position, ct);
                if (!moved.Ok)
                {
                    if (await AskAfterProblemAsync(run, moved.Problem ?? "포커서가 움직이지 않습니다", ct)) continue;
                    return Kept(ctx, ev);
                }
            }
            var points = new List<object>();
            run.Live("focus-curve", data: new { points = Array.Empty<object>(), last = ctx.Memory.LastFocus?.Position });
            var af = await devices.AutofocusAsync((pos, hfr) =>
            {
                points.Add(new { pos, hfr });
                run.Readout("hfr", $"{hfr:F1}", "별 크기 (HFR) · 이번 지점", Tone.Busy, new Dictionary<string, double> { ["hfr"] = hfr, ["position"] = pos }, live: true);
                run.Live("focus-curve", data: new { points = points.ToArray(), last = ctx.Memory.LastFocus?.Position });
                run.Status($"자동초점 중입니다 · {points.Count} / 9 지점 · AA 추천값");
            }, ct);
            if (!af.Ok || !af.StarsFound || !af.CurveGood)
            {
                var problem = af.Problem ?? (!af.StarsFound ? "자동초점에서 별을 찾지 못했습니다" : "초점 곡선이 고르지 않습니다");
                if (await AskAfterProblemAsync(run, problem, ct)) continue;
                return Kept(ctx, ev);
            }
            var temp = await devices.TemperatureAsync(ct);
            _best = af.BestPosition;
            ctx.Memory.LastFocus = (af.BestPosition, temp);
            var tempText = temp is { } t ? $" · 기온 {t:F1}°C 기억" : "";
            run.Readout("hfr", $"{af.Hfr:F1}", $"별 크기 (HFR) · 가장 좋은 위치 {af.BestPosition:N0}{tempText}", Tone.Ok,
                new Dictionary<string, double> { ["hfr"] = af.Hfr, ["position"] = af.BestPosition });
            run.Status("초점을 다시 맞췄어요", Tone.Ok);
            return new Completed(new FocusCheckResult(true, ev.TemperatureChangeC, ev.CenterHfr, af.BestPosition, af.Hfr, temp, ctx.Now()),
                $"다시 맞춤 · HFR {af.Hfr:F1}", "초점을 다시 맞췄어요",
                temp is { } tt ? $"이번 기온({tt:F1}°C)과 함께 기억했어요. 가이딩으로 넘어갑니다." : "새 위치를 기억했어요. 가이딩으로 넘어갑니다.")
            { AutoNext = true };
        }
    }

    /// <summary>다시 맞추다 안 됨: 다시 시도면 true, 그대로 진행이면 false</summary>
    private static async Task<bool> AskAfterProblemAsync(ITaskRun run, string problem, CancellationToken ct)
    {
        run.Guide("초점을 다시 맞추지 못했습니다", "구름이 지나가거나 바람에 흔들렸을 수 있습니다. 다시 해 보거나, 장비 준비 때 맞춘 초점으로 진행하세요.");
        run.Status(problem, Tone.Fail);
        return await run.AskAsync([new("retry", "다시 자동초점", true), new("keep", "그대로 진행")], ct) == "retry";
    }

    private static Completed Kept(PrepContext ctx, Evidence ev) =>
        new(new FocusCheckResult(false, ev.TemperatureChangeC, ev.CenterHfr, null, null, null, ctx.Now()), "그대로 진행",
            "그대로 진행합니다", "장비 준비 때 맞춘 초점으로 가이딩으로 넘어갑니다. 시험 사진에서 별 크기를 한 번 더 확인해요.") { AutoNext = true };

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (s.Error) return EndStateCheck.Fail("포커서가 오류(멈춤 감지) 상태입니다");
        if (s.Moving) return EndStateCheck.Fail("포커서가 아직 움직이고 있습니다");
        if (_best is { } b && s.Position != b) return EndStateCheck.Fail($"포커서가 새로 맞춘 위치({b:N0})에 있지 않습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}
