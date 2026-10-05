using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Guiding;

/// <summary>⑥ 가이딩 장비. 실제: N.I.N.A. 가이더 명령으로 시작(시퀀스가 상태를 알도록), 측정값은 PHD2에서</summary>
public interface IGuidingDevices
{
    /// <summary>별 선택 → 가이딩 시작 → 안정(Settle)까지. 단계마다 알린다</summary>
    Task<GuideStart> StartAsync(double exposureSeconds, Action<string> phase, CancellationToken ct);
    /// <summary>오차를 잰다. 샘플마다(적경″, 적위″) 알린다</summary>
    Task<GuideStats> MeasureAsync(Action<double, double> sample, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<GuidingEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>CalibrationMismatch = PHD2가 보정값이 지금 장비와 맞지 않는다고 판단</summary>
public sealed record GuideStart(bool Ok, bool StarFound, bool CalibrationMismatch = false, string? Problem = null);
public sealed record GuideStats(double RaArcsec, double DecArcsec, int StarLost)
{
    public double TotalArcsec => Math.Sqrt(RaArcsec * RaArcsec + DecArcsec * DecArcsec);
}
public sealed record GuidingEndState(bool Guiding, bool Settled, int StarLostRecently);

/// <summary>
/// ⑥ 가이딩 (DESIGN.md ⑥): 별 선택 → 안정화 → 오차 측정. 가이드 노출 시작값 1.5초(하모닉 적도의는 짧게).
/// 판정 기준 = 주 카메라 한 픽셀(″): 이하 "충분해요" / 1.5배 이하 "괜찮아요, 지켜볼게요" / 그 위 "아쉬워요".
/// </summary>
public sealed class GuidingTask(IGuidingDevices devices) : IPrepTask
{
    private const double Exposure = 1.5, LongExposure = 3;

    public string Id => "guiding";
    public string Title => "가이딩";
    public string StartLabel => "가이딩 시작";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("star", "별 선택"), new("settle", "안정화"), new("measure", "오차 측정")];

    public bool AppliesTo(PrepContext ctx) => ctx.HasGuider;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        var limit = ctx.MainPixelScaleArcsec;
        run.Live("guide-image", "/api/prepare/live/guide");
        while (true)
        {
            run.SubStep(0);
            run.Guide("별 선택", "가이드 별을 따라 적도의를 미세하게 보정합니다. 안정되면 오차를 재어 보여 드립니다.");
            run.ClearReadout();
            var exposure = Exposure;
            GuideStart start;
            while (true)
            {
                run.Status($"가이드 별을 고르는 중입니다 · 가이드 노출 {exposure:0.#}초 (AA가 정함)");
                start = await devices.StartAsync(exposure, phase =>
                {
                    if (phase == "settle") { run.SubStep(1); run.Guide("안정화", "가이딩이 안정되기를 기다립니다."); run.Status("가이딩이 안정되기를 기다리는 중입니다"); }
                }, ct);
                if (start.StarFound || exposure >= LongExposure) break;
                exposure = LongExposure;
                run.Status("별을 찾지 못해 가이드 노출을 3초로 늘려 다시 찾는 중입니다", Tone.Warn);
            }
            if (start.CalibrationMismatch)
            {
                run.Guide("캘리브레이션이 맞지 않습니다", "보정값이 지금 장비 상태와 맞지 않아 가이딩이 흔들립니다. 캘리브레이션부터 다시 하는 게 좋아요.");
                run.Status(start.Problem ?? "보정값 불일치", Tone.Fail);
                var c = await run.AskAsync([new("recal", "캘리브레이션 다시 하기", true), new("retry", "다시 시도")], ct);
                if (c == "recal") return new RedoRequest("calibration");
                continue;
            }
            if (!start.Ok || !start.StarFound)
            {
                run.Guide("가이드 별을 찾지 못했습니다", "노출을 늘려도 가이드 카메라에 별이 보이지 않습니다. 가이드 망원경 덮개·초점이나 구름을 확인한 뒤 다시 시도해 주세요.");
                run.Status(start.Problem ?? "별 없음", Tone.Fail);
                await run.AskAsync([new("retry", "다시 시도", true)], ct);
                continue;
            }

            run.SubStep(2);
            run.Guide("오차 측정", "가이딩 오차를 잽니다.");
            run.Status("가이딩 오차를 재는 중입니다");
            var series = new List<double[]>();
            var stats = await devices.MeasureAsync((ra, dec) =>
            {
                series.Add([ra, dec]);
                var total = Math.Sqrt(ra * ra + dec * dec);
                run.Readout("guiding", $"{total:F1}″", $"가이딩 오차 (전체) · 기준 {limit:F2}″ = 주 카메라 한 픽셀", Tone.Busy,
                    new Dictionary<string, double> { ["ra"] = ra, ["dec"] = dec, ["limit"] = limit }, live: true);
                run.Live("guide-image", "/api/prepare/live/guide", new { series = series.ToArray(), limit });
            }, ct);

            var (grade, tone, text) = GuidingRules.Judge(stats, limit);
            run.Readout("guiding", $"{stats.TotalArcsec:F1}″",
                $"가이딩 오차 (전체) · 적경 {stats.RaArcsec:F1}″ · 적위 {stats.DecArcsec:F1}″ · {(stats.StarLost == 0 ? "별 잃음 없음" : $"별 {stats.StarLost}번 잃음")} · 기준 {limit:F2}″",
                tone, new Dictionary<string, double> { ["total"] = stats.TotalArcsec, ["ra"] = stats.RaArcsec, ["dec"] = stats.DecArcsec, ["limit"] = limit, ["starLost"] = stats.StarLost });
            run.Status(null);
            return new Completed(new GuidingResult(stats.TotalArcsec, stats.RaArcsec, stats.DecArcsec, grade, ctx.Now()), $"{stats.TotalArcsec:F1}″ · {grade}",
                grade, text, [new("redo:guiding", "다시 재기")]);
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (!s.Guiding) return EndStateCheck.Fail("PHD2가 가이딩하고 있지 않습니다");
        if (!s.Settled) return EndStateCheck.Fail("가이딩이 아직 안정되지 않았습니다");
        if (s.StarLostRecently > 0) return EndStateCheck.Fail("방금 가이드 별을 잃었습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);

    /// <summary>끝난 뒤에도 가이딩은 계속 돈다 — 앞 작업을 다시 하거나 중단하면 러너가 먼저 멈춘다 (CX-PREP-CODE-01)</summary>
    public bool KeepsRunning => true;
}

/// <summary>가이딩 판정 (순수 함수, DESIGN.md ⑥)</summary>
public static class GuidingRules
{
    public static (string Grade, Tone Tone, string Text) Judge(GuideStats s, double limitArcsec)
    {
        var ratio = s.TotalArcsec / limitArcsec;
        if (ratio <= 1 && s.StarLost == 0)
            return ("충분해요", Tone.Ok, $"주 카메라 한 픽셀({limitArcsec:F2}″)보다 작아 사진에서 흔들림이 거의 보이지 않을 수준이에요. 다음은 시험 사진입니다.");
        if (ratio <= 1.5)
            return ("괜찮아요, 지켜볼게요", Tone.Warn, "한 픽셀보다 조금 커요. 촬영하면서 지켜볼게요.");
        return ("아쉬워요", Tone.Fail, "한 픽셀의 1.5배를 넘어요. 바람·시상·극축 오차를 의심해 볼 수 있어요. 다시 재 보거나 그대로 진행할 수 있어요.");
    }
}
