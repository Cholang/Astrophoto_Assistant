using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Center;

/// <summary>⑤ 센터링 장비. 실제: N.I.N.A. slew(center=true, RA는 도 단위) — 사진 → ASTAP 솔빙 → Sync → 보정 반복 (N.I.N.A. 솔빙 설정 그대로)</summary>
public interface ICenterDevices
{
    /// <summary>한 회차: 사진 → 위치 계산 → 적도의 보정. blind = 하늘 전체에서 찾기. 남은 오차(′)와 카메라 방향(°)</summary>
    Task<CenterAttempt> AttemptAsync(double raDeg, double decDeg, double exposureSeconds, bool blind, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<CenterEndState> ReadEndStateAsync(CancellationToken ct);
}

/// <summary>한 회차 결과. Hfr·Stars = 그 사진의 별 크기·별 수 (못 재면 null — 초점 확인이 읽음)</summary>
public sealed record CenterAttempt(bool Solved, double ErrorArcmin, double? CameraAngleDeg, double? Hfr = null, int? Stars = null);
public sealed record CenterEndState(double ErrorArcmin, bool MountMoving, bool Tracking, bool CameraExposing);

/// <summary>
/// ⑤ 센터링 (DESIGN.md ⑤): 사진·솔빙·보정 반복, 목표 1′ 안, 최대 10번. 카메라 방향은 돌리지 않고 기록만.
/// 솔빙 실패 → 노출 늘려 다시 → 하늘 전체 검색 → "초점이나 구름" 안내 + 초점 다시 맞추기(초점 확인에 넘김).
/// 맞추면 그 사진으로 구도를 보여 주고 "이 대상으로 확정 / 다른 대상"(탐색).
/// </summary>
public sealed class CenterTask(ICenterDevices devices) : IPrepTask
{
    public const double TargetArcmin = 1;
    public const int MaxAttempts = 10;
    private const double Exposure = 2, LongExposure = 4;

    public string Id => "center";
    public string Title => "센터링";
    public string StartLabel => "센터링 시작";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("shoot", "사진 촬영"), new("solve", "위치 계산"), new("correct", "중심 보정")];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        // 이동에서 "지금 위치를 목표로"를 골랐으면 가운데 맞추기도 하지 않는다 (2026-10-08 사용자 결정)
        if (ctx.Results.Get<SlewResult>() is { AcceptedHere: true }) return Skip(ctx, "지금 위치를 목표로 해서 가운데 맞추기를 하지 않아요.");
        run.Guide("사진 촬영", "사진을 찍어 실제 위치를 계산하고, 적도의를 조금씩 옮깁니다. 카메라 방향은 그대로 둡니다.");
        run.Live("solved-photo", "/api/prepare/live/photo");
        while (true)
        {
            var exposure = Exposure;
            var blind = false;
            CenterAttempt? last = null;
            for (var i = 1; i <= MaxAttempts; i++)
            {
                run.SubStep(0);
                run.Status($"사진을 찍는 중입니다 · {i}회차 · 노출 {exposure:0.#}초 · 목표 1′ 안 · 최대 {MaxAttempts}번");
                run.SubStep(1);
                CenterAttempt a;
                await using (await ctx.Mount.AcquireAsync(ct))
                    a = await devices.AttemptAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees, exposure, blind, ct);
                if (!a.Solved)
                {
                    if (exposure < LongExposure) { exposure = LongExposure; run.Status("위치를 계산하지 못해 노출을 4초로 늘려 다시 찍습니다", Tone.Warn); i--; continue; }
                    if (!blind) { blind = true; run.Status("다시 실패 · 하늘 전체에서 찾는 중입니다 (전체 하늘 검색)", Tone.Warn); i--; continue; }
                    run.Guide("위치를 계산하지 못했습니다", "노출을 늘리고 하늘 전체에서 찾아도 별 배치를 찾지 못했습니다. 초점이 나갔거나 구름이 지나가는 중일 수 있습니다. 초점이나 구름을 확인해 주세요.");
                    run.Status("센터링에 실패했습니다", Tone.Fail);
                    var c = await run.AskAsync([new("retry", "다시 시도", true), new("refocus", "초점 다시 맞추기"), new("skip", "센터링 건너뛰기")], ct);
                    if (c == "skip") return Skip(ctx, "대상이 가운데에서 벗어나 있을 수 있어요. 시험 사진에서 구도를 확인해 주세요.");
                    if (c == "refocus")
                    {
                        // 초점 확인이 비교 없이 바로 다시 맞춘다 (장비 준비의 초점은 그대로)
                        ctx.RefocusRequested = true;
                        return new RedoRequest("focuscheck");
                    }
                    goto NextRound;
                }
                run.SubStep(2);
                last = a;
                run.Readout("center-error", $"{a.ErrorArcmin:F1}′", "대상과 화면 가운데의 거리 · 목표 1′ 안", a.ErrorArcmin <= TargetArcmin ? Tone.Ok : Tone.Busy,
                    new Dictionary<string, double> { ["errorArcmin"] = a.ErrorArcmin, ["attempt"] = i });
                if (a.ErrorArcmin <= TargetArcmin)
                {
                    if (a.CameraAngleDeg is { } angle) ctx.Memory.SetCameraAngle(ctx.Plan.TargetId, angle);
                    var angleText = a.CameraAngleDeg is { } ang ? $" · 카메라 방향 {ang:F0}°" : "";
                    run.Readout("center-error", $"{a.ErrorArcmin:F1}′", $"대상과 화면 가운데의 거리 · 목표 1′ 안{angleText}", Tone.Ok,
                        new Dictionary<string, double> { ["errorArcmin"] = a.ErrorArcmin, ["attempt"] = i });
                    run.Status(null);
                    // 탐색: 이 사진(센터링 사진)으로 구도를 보고 이 대상으로 확정하거나 다른 대상으로 (DESIGN.md "단계 재구성")
                    return new Completed(new CenterResult(a.ErrorArcmin, a.CameraAngleDeg, i, ctx.Now(), a.Hfr, a.Stars), $"오차 {a.ErrorArcmin:F1}′{angleText}",
                        "이 대상으로 확정할까요?", "가운데에 맞춘 사진이에요. 구도가 마음에 들면 확정하세요. 다른 대상을 고르면 계획으로 돌아가고, 장비 준비는 그대로 씁니다.",
                        [new("flow:replan", "다른 대상")]);
                }
                run.Status("적도의를 조금 옮기는 중입니다");
            }
            run.Guide("가운데에 맞추지 못했습니다", $"{MaxAttempts}번 보정해도 목표(1′) 안에 들어오지 않았습니다. 적도의가 바람에 흔들리거나 케이블이 당기고 있을 수 있습니다.");
            run.Status($"마지막 오차 {last?.ErrorArcmin:F1}′", Tone.Fail);
            if (await run.AskAsync([new("retry", "다시 시도", true), new("skip", "센터링 건너뛰기")], ct) == "skip")
                return Skip(ctx, $"대상이 가운데에서 {last?.ErrorArcmin:F1}′쯤 벗어나 있어요. 시험 사진에서 구도를 확인해 주세요.");
        NextRound:;
        }
    }

    /// <summary>센터링 건너뛰기 (2026-10-08 사용자 결정)</summary>
    private static Completed Skip(PrepContext ctx, string why) =>
        // 탐색의 "이 대상으로 확정 / 다른 대상"은 그대로 묻는다
        new(new CenterResult(0, null, 0, ctx.Now(), Skipped: true), "건너뜀", "센터링을 건너뛰었어요",
            why + " 이 대상으로 확정하거나 다른 대상을 고르세요.", [new("flow:replan", "다른 대상")]);

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        if (!s.Tracking) return EndStateCheck.Fail("적도의 추적이 꺼져 있습니다");
        if (s.CameraExposing) return EndStateCheck.Fail("카메라가 아직 노출 중입니다");
        if (ctx.Results.Get<CenterResult>() is not { Skipped: true } && s.ErrorArcmin > TargetArcmin) return EndStateCheck.Fail($"대상이 가운데에서 {s.ErrorArcmin:F1}′ 벗어나 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}
