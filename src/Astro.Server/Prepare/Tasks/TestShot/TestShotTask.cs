using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.TestShot;

/// <summary>⑦ 시험 사진 장비. 실제: N.I.N.A. 촬영(주 카메라 X-T5) · 이미지 통계, 파일은 같은 밤 폴더의 "시험"</summary>
public interface ITestShotDevices
{
    /// <summary>계획 노출로 한 장. 남은 초를 알린다</summary>
    Task<bool> ExposeAsync(int exposureSeconds, int iso, Action<int> remaining, CancellationToken ct);
    /// <summary>
    /// since(이번 노출을 시작한 시각) 뒤에 저장된 사진만 내려받고 검사한다. 못 받거나 그보다 오래된 사진뿐이면 null
    /// — 노출이 실패했는데 이전 사진을 이번 시험 사진으로 받아들이지 않게 (CX-PREP-CODE-05)
    /// </summary>
    Task<ShotStats?> DownloadAndAnalyzeAsync(DateTimeOffset since, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<TestShotEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>사진 검사값. Background = 배경 밝기(0~1), Eccentricity = 별 길쭉함(0 둥긂 ~ 1)</summary>
public sealed record ShotStats(double Hfr, double Eccentricity, double Background, double SaturatedPercent, double GuidingRms, string FilePath);
public sealed record TestShotEndState(bool CameraExposing, bool Guiding, bool FileSaved);

/// <summary>
/// ⑦ 시험 사진 (DESIGN.md ⑦): 계획 노출로 한 장 → AA가 규칙으로 바로 검사 → "좋아요" 또는 문제 + 할 일.
/// 노출을 바꿔야 하면 묻고(계획에 반영) 다시 찍기. 첫 장으로 쓰지 않는다. 끝 버튼은 러너의 "촬영 시작".
/// </summary>
public sealed class TestShotTask(ITestShotDevices devices) : IPrepTask
{
    public string Id => "test";
    public string Title => "시험 사진";
    public string StartLabel => "시험 사진 찍기";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("expose", "시험 노출"), new("download", "사진 받기"), new("check", "사진 검사"), new("result", "결과 확인")];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        while (true)
        {
            run.SubStep(0);
            run.Guide("시험 노출", "계획한 노출 그대로 한 장 찍어 초점·추적·구도를 확인합니다. 이 사진은 촬영 사진에 넣지 않습니다.");
            run.Live("none");
            run.Status($"노출 중입니다 · {ctx.ExposureSeconds}초 · ISO {ctx.Plan.Iso}");
            var since = ctx.Now();
            var exposed = await devices.ExposeAsync(ctx.ExposureSeconds, ctx.Plan.Iso, s =>
                run.Readout("countdown", $"{s}초", "남은 노출", Tone.Busy, new Dictionary<string, double> { ["remaining"] = s }, live: true), ct);
            if (!exposed)
            {
                // 노출이 실패하면 내려받기·검사로 가지 않는다 (CX-PREP-CODE-05)
                run.ClearReadout();
                run.Guide("시험 사진을 찍지 못했습니다", "카메라가 노출하지 못했습니다. 카메라 전원·USB 연결과 카메라의 PC 연결 방식(테더링)을 확인한 뒤 다시 찍어 주세요.");
                run.Status("카메라가 노출하지 못했습니다", Tone.Fail);
                await run.AskAsync([new("retry", "다시 찍기", true)], ct);
                continue;
            }

            run.SubStep(1);
            run.Guide("사진 받기", "카메라에서 사진을 내려받습니다.");
            run.Status("사진을 내려받는 중입니다");
            var stats = await devices.DownloadAndAnalyzeAsync(since, ct);
            if (stats is null)
            {
                run.Status("사진을 내려받지 못해 한 번 더 받아 오는 중입니다", Tone.Warn);
                stats = await devices.DownloadAndAnalyzeAsync(since, ct);
            }
            if (stats is null)
            {
                run.Guide("사진을 받지 못했습니다", "두 번 시도했지만 카메라에서 사진을 내려받지 못했습니다. 카메라 USB 연결과 전원, 카메라 화질 설정이 RAW인지 확인한 뒤 다시 찍어 주세요 (JPEG면 사진을 쓸 수 없습니다).");
                run.Status("시험 사진을 찍지 못했습니다", Tone.Fail);
                await run.AskAsync([new("retry", "다시 찍기", true)], ct);
                continue;
            }

            run.SubStep(2);
            run.Live("test-photo", $"/api/prepare/live/test?f={Uri.EscapeDataString(stats.FilePath)}");
            // 대상 단계에서 초점을 다시 맞췄으면 그 값과 비교한다
            var focusHfr = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, Hfr: { } rh } ? rh : ctx.Results.Get<FocusResult>()?.Hfr;
            var verdict = TestShotRules.Judge(stats, focusHfr, ctx.ExposureSeconds);
            var values = new Dictionary<string, double>
            {
                ["hfr"] = stats.Hfr, ["eccentricity"] = stats.Eccentricity, ["background"] = stats.Background,
                ["saturatedPercent"] = stats.SaturatedPercent, ["guidingRms"] = stats.GuidingRms,
            };
            if (focusHfr is { } fh) values["focusHfr"] = fh;
            run.Readout("test-shot", $"HFR {stats.Hfr:F1}", verdict.Caption, verdict.Tone, values);

            run.SubStep(3);
            if (verdict.SuggestExposure is { } shorter)
            {
                run.Guide("배경이 밝아요", $"달빛 때문에 하늘 배경이 밝습니다. 노출을 줄이면 배경이 덜 하얗게 뜨고 별도 덜 포화됩니다. 노출 {ctx.ExposureSeconds}초 → {shorter}초로 바꿀까요? (계획에 반영)");
                run.Status("배경 밝기 높음", Tone.Warn);
                var c = await run.AskAsync([new("shorter", $"{shorter}초로 바꾸고 다시 찍기", true), new("keep", "그대로 진행")], ct);
                if (c == "shorter") { ctx.ExposureSeconds = shorter; continue; }
            }
            else if (verdict.Problem is { } problem)
            {
                run.Guide(problem.Title, problem.Text);
                run.Status(problem.Status, Tone.Warn);
                var c = await run.AskAsync([new("retry", "다시 찍기", true), new("keep", "그대로 진행"), new(problem.RedoAction, problem.RedoLabel)], ct);
                if (c == "retry") continue;
                if (c == problem.RedoAction)
                {
                    if (problem.RedoAction == "redo:focuscheck") ctx.RefocusRequested = true; // 비교 없이 바로 다시 맞춘다
                    return new RedoRequest(problem.RedoAction[5..]);
                }
            }
            run.Status(null);
            var result = new TestShotResult(ctx.ExposureSeconds, stats.Hfr, stats.Eccentricity, stats.SaturatedPercent, stats.FilePath, ctx.Now());
            return new Completed(result, $"좋아요 · HFR {stats.Hfr:F1} · {ctx.ExposureSeconds}초", "좋아요, 촬영을 시작해도 돼요",
                $"초점·별 모양·배경·가이딩 모두 괜찮습니다. 끝나는 시각은 {ctx.Plan.End:HH:mm}입니다. 귀퉁이 별까지 보려면 9칸 확대 보기를 누르세요.",
                [new("redo:test", "다시 찍기"), new("redo:center", "다시 센터링")]);
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (s.CameraExposing) return EndStateCheck.Fail("카메라가 아직 노출 중입니다");
        if (ctx.HasGuider && !s.Guiding) return EndStateCheck.Fail("가이딩이 멈춰 있습니다");
        if (!s.FileSaved) return EndStateCheck.Fail("시험 사진 파일이 저장되지 않았습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>시험 사진 판정 (순수 함수). 기준은 실기에서 다듬는다</summary>
public static class TestShotRules
{
    public const double HfrGrowthLimit = 1.25;   // 초점 때보다 25% 넘게 커지면 초점이 밀렸다고 본다
    public const double EccentricityLimit = 0.6; // 이보다 길쭉하면 가이딩·추적 문제
    public const double BackgroundLimit = 0.35;  // 배경이 이보다 밝으면 노출을 줄이자고 한다

    public sealed record Problem(string Title, string Text, string Status, string RedoAction, string RedoLabel);
    public sealed record Verdict(string Caption, Tone Tone, int? SuggestExposure, Problem? Problem);

    public static Verdict Judge(ShotStats s, double? focusHfr, int exposureSeconds)
    {
        var parts = new List<string>();
        if (focusHfr is { } f) parts.Add($"초점 때 {f:F1}");
        parts.Add(s.Eccentricity <= EccentricityLimit ? "별 둥긂" : "별 길쭉함");
        parts.Add(s.Background <= BackgroundLimit ? "배경 적당" : "배경 밝음");
        parts.Add($"포화 {s.SaturatedPercent:F1}%");
        parts.Add($"가이딩 {s.GuidingRms:F1}″");
        parts.Add("\"시험\" 폴더에 보관함");
        var caption = string.Join(" · ", parts);

        if (s.Background > BackgroundLimit && exposureSeconds > 30)
            return new Verdict(caption, Tone.Warn, (int)Math.Round(exposureSeconds * 0.75 / 10) * 10, null);
        if (focusHfr is { } fh && s.Hfr > fh * HfrGrowthLimit)
            return new Verdict(caption, Tone.Warn, null, new("초점이 밀렸어요", $"별 크기가 초점을 맞출 때({fh:F1})보다 커졌습니다({s.Hfr:F1}). 기온이 바뀌었을 수 있어요.", "별 크기 증가", "redo:focuscheck", "초점 다시 맞추기"));
        if (s.Eccentricity > EccentricityLimit)
            return new Verdict(caption, Tone.Warn, null, new("별이 길쭉해요", "별이 한쪽으로 늘어졌습니다. 가이딩이나 추적이 흔들렸을 수 있어요.", "별 모양 길쭉함", "redo:guiding", "가이딩 다시 재기"));
        return new Verdict(caption, Tone.Ok, null, null);
    }
}
