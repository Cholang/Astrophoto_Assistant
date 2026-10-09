using Astro.Server.Prepare.Flow;
using Astro.Server.Sky;

namespace Astro.Server.Prepare.Tasks.Calibration;

/// <summary>② 캘리브레이션 장비. 실제: N.I.N.A.(이동, RA는 도 단위) · PHD2(별 선택·캘리브레이션)</summary>
public interface ICalibrationDevices
{
    /// <summary>시간각(시간)·적위(도)로 이동. 도착 오차(도)를 돌려준다</summary>
    Task<SlewOutcome> SlewToHourAngleAsync(double hourAngle, double decDeg, CancellationToken ct);
    /// <summary>가이드 별을 고른다 (노출은 AA가 정함, 별이 없으면 늘려 다시)</summary>
    Task<StarPick> SelectStarAsync(CancellationToken ct);
    /// <summary>캘리브레이션. 걸음마다 알린다 (방향 "west"/"north", 걸음 번호)</summary>
    Task<CalibrationData> CalibrateAsync(Action<string, int> step, CancellationToken ct);
    /// <summary>PHD2에 이번 밤에 쓸 수 있는 보정값이 남아 있는가 (PHD2를 다시 켰으면 없음)</summary>
    Task<bool> HasCalibrationAsync(CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<CalibrationEndState> ReadEndStateAsync(CancellationToken ct);
}

public sealed record SlewOutcome(bool Ok, double ArrivalErrorDeg, string? Problem = null);
public sealed record StarPick(bool Found, double Snr, double ExposureSeconds);
/// <summary>캘리브레이션 결과. Warnings = PHD2 경고(직교 오차·두 축 속도 차·Dec 백래시)</summary>
public sealed record CalibrationData(bool Ok, double OrthogonalityErrorDeg, double RaRate, double DecRate, IReadOnlyList<string> Warnings, string? Problem = null);
public sealed record CalibrationEndState(bool HasCalibration, bool Phd2Guiding, bool Phd2Looping, bool MountMoving, bool Tracking);

/// <summary>
/// ② 캘리브레이션 (DESIGN.md ②): 위치로 이동 → 별 선택 → 움직임 측정. 자동.
/// 위치 A(자오선 서쪽 1시간·적위 0°) → 안 되면 B(동쪽 1시간). 같은 밤 대상만 바꾸면 재사용.
/// </summary>
public sealed class CalibrationTask(ICalibrationDevices devices) : IPrepTask
{
    public const double ArrivalLimitDeg = 2;
    private static readonly (string Name, string Label, double HourAngle)[] Positions =
        [("A", "자오선 서쪽 1시간 · 적위 0°", +1), ("B", "자오선 동쪽 1시간 · 적위 0°", -1)];

    public string Id => "calibration";
    public string Title => "캘리브레이션";
    // 누르면 적도의가 크게 움직이므로 어디로 가는지 버튼에 쓴다. 남쪽이 막힌 곳이면 옆의 "가이딩 없이 진행" (2026-10-09 사용자 제안 — 따로 묻지 않고 버튼에서)
    public string StartLabel => "캘리브레이션 시작 · 남쪽 하늘로 이동";
    public IReadOnlyList<PrepAction> StartAlternatives { get; } = [new("next:noguide", "가이딩 없이 진행")];
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("move", "위치로 이동"), new("star", "별 선택"), new("measure", "움직임 측정")];

    public bool AppliesTo(PrepContext ctx) => ctx.HasGuider;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        var evening = TonightService.EveningOf(ctx.Now());
        // 앞 작업 끝에서 "가이딩 없이 진행"을 골랐으면 움직이지 않고 끝낸다
        if (ctx.StartChoice == "noguide")
        {
            ctx.StartChoice = null;
            return await NoGuideAsync(ctx, evening, "가이딩 없이 진행을 고름", ct);
        }
        ctx.StartChoice = null;
        ctx.Results.Remove(typeof(GuiderSkipped)); // 캘리브레이션을 다시 하면 가이딩도 다시 쓴다
        // 같은 밤 + 마지막 극축 정렬 뒤에 만든 보정값만 재사용 (극축 정렬을 다시 했으면 새로, CX-PREP-CODE-06)
        if (ctx.Memory.LastCalibration is { } last && last.Evening == evening && last.At > (ctx.Memory.LastPolarAlignedAt ?? DateTimeOffset.MinValue)
            && await devices.HasCalibrationAsync(ct))
        {
            run.Guide("이번 밤 보정값", "오늘 극축 정렬 뒤에 만든 보정값이 있습니다. 같은 밤 대상만 바꿀 때는 다시 하지 않습니다.");
            run.Status(null);
            var choice = await run.AskAsync([new("reuse", "이번 밤 보정값 쓰기", true), new("new", "새로 하기")], ct);
            if (choice == "reuse")
            {
                var reused = last with { Reused = true, At = ctx.Now() };
                return new Completed(reused, "이번 밤 보정값 재사용", "보정값을 다시 씁니다", "같은 밤 보정값을 그대로 씁니다. 다음은 이 근처 별로 초점을 맞춥니다.");
            }
        }

        // 극축 정렬을 건너뛰었으면 움직이기 전에 한 번 묻는다 — 실내·북쪽만 트인 곳처럼 이미 안 될 걸 아는 경우 (2026-10-08 실기)
        if (ctx.Results.Get<PolarResult>() is { Skipped: true })
        {
            run.Guide("캘리브레이션을 할까요?", "적도의를 자오선 근처 하늘로 옮겨 가이드 별로 보정값을 만들어요. 그쪽 하늘이 보이지 않으면 가이딩 없이 진행할 수 있어요.");
            run.Status(null);
            if (await run.AskAsync([new("start", "캘리브레이션 시작", true), new("noguide", "가이딩 없이 진행")], ct) == "noguide")
                return await NoGuideAsync(ctx, evening, "가이딩 없이 진행을 고름", ct);
        }

        while (true)
        {
            foreach (var pos in Positions)
            {
                var data = await TryAtAsync(run, pos, ct);
                if (data is null) continue; // 이 위치는 안 됨 → 다음 위치
                var (grade, tone, reason) = CalibrationRules.Judge(data);
                var result = new CalibrationResult(false, data.OrthogonalityErrorDeg, pos.Name, evening, ctx.Now());
                ctx.Memory.LastCalibration = result;
                run.Readout("calibration", grade, reason, tone,
                    new Dictionary<string, double> { ["orthoDeg"] = data.OrthogonalityErrorDeg, ["raRate"] = data.RaRate, ["decRate"] = data.DecRate });
                if (tone == Tone.Ok)
                    return new Completed(result, $"{grade} · 직교 오차 {data.OrthogonalityErrorDeg:F1}°", grade,
                        "가이딩 보정값을 만들었습니다. 대상을 바꿔도 이 보정값을 그대로 씁니다. 다음은 이 근처 별로 초점을 맞춥니다.", [new("redo:calibration", "다시 하기")]);
                run.Guide("다시 하는 게 좋아요", "보정값이 정확하지 않을 수 있습니다.");
                run.Status(reason, Tone.Warn);
                var c = await run.AskAsync([new("redo", "다시 하기", true), new("accept", "그래도 진행")], ct);
                if (c == "accept")
                    return new Completed(result, $"{grade} · 그대로 진행", "그대로 진행합니다", "경고가 있는 보정값으로 진행합니다. 가이딩이 흔들리면 다시 하세요.", [new("redo:calibration", "다시 하기")]);
                goto Retry;
            }
            run.Guide("캘리브레이션을 하지 못했습니다", "두 위치 모두에서 별을 찾지 못했습니다.");
            run.Status("구름이 지나가는지, 가이드 망원경 덮개와 초점을 확인해 주세요", Tone.Fail);
            // 오늘 밤 보정값이 있으면 위에서 이미 물었다 — 지난밤 보정값은 조립하며 카메라 방향이 바뀌었을 수 있어 쓰지 않는다 (2026-10-08 결정)
            if (await run.AskAsync([new("retry", "다시 시도", true), new("noguide", "가이딩 없이 진행")], ct) == "noguide")
                return await NoGuideAsync(ctx, evening, "캘리브레이션을 하지 못함", ct);
        Retry:;
        }
    }

    /// <summary>가이딩 없이 진행: PHD2 루프·적도의 이동을 멈춰 두고 그날 밤 가이딩을 쓰지 않는다</summary>
    private async Task<TaskOutcome> NoGuideAsync(PrepContext ctx, DateOnly evening, string why, CancellationToken ct)
    {
        await devices.StopAsync(ct);
        ctx.Results.Set(new GuiderSkipped(why, ctx.Now()));
        return new Completed(new CalibrationResult(false, null, "", evening, ctx.Now(), Skipped: true), "건너뜀 · 가이딩 없이",
            "가이딩 없이 진행합니다", "오늘 밤은 가이딩 없이 찍습니다. 노출은 대상 단계에서 짧게 고를 수 있어요. 다음은 초점입니다.") { AutoNext = true };
    }

    /// <summary>
    /// 이어서 할 때: 보정값이 PHD2에 남아 있고 PHD2·적도의가 멈춰 있으면 건너뜀. 추적은 보지 않는다 — 장비 준비가 끝나면 적도의를 홈에 두고 추적을 끄므로 (2026-10-08)
    /// </summary>
    public async Task<EndStateCheck> CheckResumeAsync(PrepContext ctx, CancellationToken ct)
    {
        if (ctx.Results.Get<CalibrationResult>() is { Skipped: true }) return EndStateCheck.Pass;
        var s = await devices.ReadEndStateAsync(ct);
        if (!s.HasCalibration) return EndStateCheck.Fail("PHD2에 보정값이 없습니다");
        if (s.Phd2Guiding || s.Phd2Looping) return EndStateCheck.Fail("PHD2가 아직 가이딩·루프 중입니다");
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        return EndStateCheck.Pass;
    }

    /// <summary>한 위치에서 이동 → 별 → 측정. 이 위치가 안 되면 null (다음 위치로)</summary>
    private async Task<CalibrationData?> TryAtAsync(ITaskRun run, (string Name, string Label, double HourAngle) pos, CancellationToken ct)
    {
        run.SubStep(0);
        run.Guide("위치로 이동", $"극 근처는 캘리브레이션에 맞지 않아 위치 {pos.Name}({pos.Label})로 옮깁니다. 손대실 것은 없습니다.");
        run.ClearReadout();
        run.Live("sky-map");
        run.Status("이동 중입니다");
        SlewOutcome moved;
        await using (await run.Context.Mount.AcquireAsync(ct))
            moved = await devices.SlewToHourAngleAsync(pos.HourAngle, 0, ct);
        if (!moved.Ok || moved.ArrivalErrorDeg > ArrivalLimitDeg)
        {
            run.Status($"위치 {pos.Name}에 제대로 가지 못했습니다 — 다음 위치로 옮깁니다", Tone.Warn);
            return null;
        }

        run.SubStep(1);
        run.Guide("별 선택", "PHD2가 가이드 카메라 사진에서 캘리브레이션에 쓸 별을 고릅니다.");
        run.Live("guide-image", "/api/prepare/live/guide");
        run.Status("별을 고르는 중입니다");
        var star = await devices.SelectStarAsync(ct);
        if (!star.Found)
        {
            run.Status($"위치 {pos.Name}에서 별을 찾지 못했습니다 (가려졌을 수 있음) — 다음 위치로 옮깁니다", Tone.Warn);
            return null;
        }
        run.Readout("star", double.IsFinite(star.Snr) ? $"SNR {star.Snr:F0}" : "별을 골랐습니다", $"가이드 노출 {star.ExposureSeconds:0.#}초 (별 신호를 보고 AA가 정함)", Tone.Ok,
            new Dictionary<string, double> { ["snr"] = star.Snr, ["exposure"] = star.ExposureSeconds });

        run.SubStep(2);
        run.Guide("움직임 측정", "PHD2가 적도의를 조금씩 밀며 별이 어느 쪽으로 얼마나 움직이는지 잽니다.");
        run.Status("별의 움직임을 재는 중입니다 · 2~5분");
        CalibrationData data;
        await using (await run.Context.Mount.AcquireAsync(ct))
            data = await devices.CalibrateAsync((dir, n) => run.Readout("steps", $"{(dir == "west" ? "서쪽" : "북쪽")} {n} / 12", "적도의를 조금씩 밀며 재는 중", Tone.Busy,
                new Dictionary<string, double> { ["step"] = n, ["north"] = dir == "west" ? 0 : 1 }, live: true), ct); // 그림 방향은 문장이 아니라 값으로
        if (!data.Ok)
        {
            run.Status($"{data.Problem} — 다음 위치로 옮깁니다", Tone.Warn);
            return null;
        }
        run.Status(null);
        return data;
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (ctx.Results.Get<CalibrationResult>() is { Skipped: true }) // 건너뜀: 보정값은 없어도 됨, 장비는 멈춰 있어야
            return s.MountMoving ? EndStateCheck.Fail("적도의가 아직 움직이고 있습니다") : s.Phd2Guiding || s.Phd2Looping ? EndStateCheck.Fail("PHD2가 아직 가이딩·루프 중입니다") : EndStateCheck.Pass;
        if (!s.HasCalibration) return EndStateCheck.Fail("PHD2에 보정값이 없습니다");
        if (s.Phd2Guiding || s.Phd2Looping) return EndStateCheck.Fail("PHD2가 아직 가이딩·루프 중입니다");
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        if (!s.Tracking) return EndStateCheck.Fail("적도의 추적이 꺼져 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>캘리브레이션 판정 (순수 함수). 기준은 PHD2 권장값 부근 — 실기에서 다듬는다</summary>
public static class CalibrationRules
{
    public const double OrthoLimitDeg = 10;
    public const double RateRatioMin = 0.6;

    public static (string Grade, Tone Tone, string Reason) Judge(CalibrationData d)
    {
        var ratio = d.RaRate > 0 && d.DecRate > 0 ? Math.Min(d.RaRate, d.DecRate) / Math.Max(d.RaRate, d.DecRate) : 0;
        if (d.Warnings.Count > 0) return ("다시 하는 게 좋아요", Tone.Warn, $"PHD2 경고: {string.Join(", ", d.Warnings)}");
        if (d.OrthogonalityErrorDeg > OrthoLimitDeg) return ("다시 하는 게 좋아요", Tone.Warn, $"두 축이 직각에서 {d.OrthogonalityErrorDeg:F1}° 벗어났습니다");
        if (ratio < RateRatioMin) return ("다시 하는 게 좋아요", Tone.Warn, "두 축의 움직임 속도 차이가 큽니다");
        return ("좋아요", Tone.Ok, $"두 축이 거의 직각 · 직교 오차 {d.OrthogonalityErrorDeg:F1}° · 두 축 속도 비슷 · PHD2 경고 없음");
    }
}
