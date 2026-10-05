using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Polar;

/// <summary>① 극축 정렬 장비 (DESIGN.md ①). 실제: N.I.N.A.(추적·RA 회전) · PHD2(카메라 넘겨주기) · SharpCap(실행·스크립트·창 캡처)</summary>
public interface IPolarDevices
{
    Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct);
    /// <summary>PHD2가 가이드 카메라를 놓게 한다 (루프 멈춤 → Stopped 기다림 → 연결 해제). 한 번 시도</summary>
    Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct);
    /// <summary>SharpCap을 카메라·스크립트와 함께 실행하고 극축 정렬을 켠다</summary>
    Task<DeviceResult> StartSharpCapAsync(CancellationToken ct);
    /// <summary>첫 사진 → RA 회전 → 둘째 사진. 진행(별 개수·노출·회전 각도)을 알린다. 별이 모자라면 노출을 늘려 다시</summary>
    Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct);
    /// <summary>정렬 중 조절량을 계속 받는다 (SharpCap 스크립트 → AA)</summary>
    IAsyncEnumerable<PolarOffset> WatchOffsetsAsync(CancellationToken ct);
    /// <summary>SharpCap 닫기 → PHD2에 가이드 카메라 재연결 → N.I.N.A.–PHD2 연결 확인</summary>
    Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct);
    /// <summary>가이드 화면 배율(″/px). 모르면 null</summary>
    Task<double?> GuidePixelScaleAsync(CancellationToken ct);
    /// <summary>조절량 단위·환산을 실기로 확인했는가 (CX-PREP-IMPL-03). 확인 전이면 등급 말을 쓰지 않는다</summary>
    bool OffsetUnitVerified { get; }
    Task<bool> StopAsync(CancellationToken ct);
    Task<PolarEndState> ReadEndStateAsync(CancellationToken ct);
}

public sealed record PoleProgress(string Stage, int Stars, double ExposureSeconds, double RotationDeg);
public sealed record PoleFindResult(bool Ok, string? Problem = null);
/// <summary>조절량(px): X = 방위(+는 왼쪽으로), Y = 고도(+는 아래로)</summary>
public sealed record PolarOffset(double XPx, double YPx, DateTimeOffset At);
public sealed record PolarEndState(bool SharpCapClosed, bool GuideCameraOnPhd2, bool Phd2Looping, bool NinaGuiderConnected, bool MountMoving);

/// <summary>① 극축 정렬: 넘겨받기 → 극 찾기 → 정렬(사용자) → 돌려주기. 사용자는 나사 조절과 "정렬 완료"만</summary>
public sealed class PolarTask(IPolarDevices devices) : IPrepTask
{
    private const int HandOverTries = 3;
    /// <summary>이보다 오래 갱신되지 않은 조절량으로는 완료하지 않는다</summary>
    private static readonly TimeSpan OffsetStaleAfter = TimeSpan.FromSeconds(5);

    public string Id => "polar";
    public string Title => "극축 정렬";
    public string StartLabel => "극축 정렬 시작";
    public IReadOnlyList<SubStep> SubSteps { get; } =
        [new("handover", "넘겨받기"), new("find", "극 찾기"), new("align", "정렬"), new("giveback", "돌려주기")];

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        await HandOverAsync(run, ct);
        await FindPoleAsync(run, ct);
        var offset = await AlignAsync(run, ct);
        await GiveBackAsync(run, ct);
        var scale = await devices.GuidePixelScaleAsync(ct);
        var (arcmin, grade) = Evaluate(offset, scale, run.Context);
        var result = new PolarResult(arcmin, offset.XPx, offset.YPx, grade, run.Context.Now());
        run.Context.Memory.LastPolarAlignedAt = run.Context.Now(); // 이 뒤로는 새 캘리브레이션 (CX-PREP-CODE-06)
        var line = arcmin is { } a ? $"{grade} · {a:F1}′" : $"{Math.Abs(offset.XPx):F0}·{Math.Abs(offset.YPx):F0}px";
        return new Completed(result, line, grade ?? "정렬 완료",
            "가이드 카메라가 PHD2에 다시 연결되었습니다. 다음은 가이딩 보정값을 만드는 캘리브레이션입니다.");
    }

    private async Task HandOverAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(0);
        run.Guide("넘겨받기", "가이드 카메라를 PHD2에서 SharpCap으로 넘깁니다. 손대실 것은 없습니다.");
        run.Live("none");
        while (true)
        {
            run.Status("적도의 추적 속도를 항성으로 맞추는 중입니다");
            await devices.SetSiderealTrackingAsync(ct); // 실패해도 진행 (DESIGN.md ①)
            var released = DeviceResult.Fail("PHD2가 가이드 카메라를 놓지 않았습니다.");
            for (var i = 1; i <= HandOverTries && !released.Ok; i++)
            {
                run.Status(i == 1 ? "PHD2가 가이드 카메라를 놓게 하는 중입니다" : $"PHD2가 가이드 카메라를 놓게 하는 중입니다 ({i}/{HandOverTries})");
                released = await devices.HandOverGuideCameraAsync(ct);
            }
            if (released.Ok)
            {
                run.Status("SharpCap을 실행하는 중입니다");
                var started = await devices.StartSharpCapAsync(ct);
                if (started.Ok) return;
                run.Status($"SharpCap을 실행하지 못했습니다. {started.Problem}".Trim(), Tone.Fail);
            }
            else run.Status($"{released.Problem} PHD2의 장비 연결 창에서 가이드 카메라 연결을 해제한 뒤 다시 시도해 주세요.", Tone.Fail);
            await run.AskAsync([new("retry", "다시 시도", true)], ct);
        }
    }

    private async Task FindPoleAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(1);
        run.Guide("극 찾기", "회전 전후의 별 위치를 비교해 극축 오차를 계산합니다. 잠시 기다려 주세요.");
        run.Live("sharpcap", "/api/prepare/live/sharpcap");
        while (true)
        {
            var found = await devices.FindPoleAsync(p =>
            {
                run.Status(p.Stage switch
                {
                    "rotate" => $"적경축을 돌리는 중입니다 ({p.RotationDeg:F0}°)",
                    // 별 개수는 모의만 (SharpCap은 알려 주지 않음)
                    _ => p.Stars > 0 ? $"별을 찾는 중입니다 · 별 {p.Stars}개 · 노출 {p.ExposureSeconds:0.#}초 (AA가 정함)" : $"별을 찾는 중입니다 · 노출 {p.ExposureSeconds:0.#}초 (AA가 정함)",
                });
            }, ct);
            if (found.Ok) return;
            run.Status(found.Problem ?? "별을 찾지 못했습니다. 가이드 망원경 덮개와 구름, 북쪽 시야를 확인해 주세요.", Tone.Fail);
            await run.AskAsync([new("retry", "다시 시도", true)], ct);
        }
    }

    /// <summary>
    /// 사용자가 "정렬 완료"를 누를 때까지 조절량을 보여 준다. 마지막 값을 돌려준다.
    /// 조절량을 아직 못 받았거나 한동안 갱신되지 않았으면 완료하지 않고 기다리게 한다 — 없는 값을 0·좋은 등급으로 저장하지 않는다 (CX-PREP-CODE-04)
    /// </summary>
    private async Task<PolarOffset> AlignAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(2);
        run.Guide("정렬", "화살표 방향으로 나사를 조절해 오차를 줄이고, 원하는 만큼 맞추면 정렬 완료를 누르세요.");
        run.Status(null);
        var scale = await devices.GuidePixelScaleAsync(ct);
        PolarOffset? last = null;
        var arrived = new SemaphoreSlim(0);
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = Task.Run(async () =>
        {
            await foreach (var o in devices.WatchOffsetsAsync(watch.Token))
            {
                last = o;
                ShowOffset(run, o, scale);
                arrived.Release();
            }
        }, CancellationToken.None);
        try
        {
            while (true)
            {
                await run.AskAsync([new("align-done", "정렬 완료", true)], ct);
                var got = last;
                if (got is null)
                    run.Status("아직 조절량을 받지 못했습니다. SharpCap 화면에 조절 화살표가 나오면 다시 눌러 주세요", Tone.Warn);
                else if (run.Context.Now() - got.At > OffsetStaleAfter)
                    run.Status($"조절량이 {(run.Context.Now() - got.At).TotalSeconds:F0}초째 갱신되지 않습니다. SharpCap 화면을 확인한 뒤 다시 눌러 주세요", Tone.Warn);
                else
                {
                    run.Status(null);
                    return got;
                }
                // 새 값이 오거나 잠시 지나면 다시 묻는다
                await arrived.WaitAsync(TimeSpan.FromSeconds(1), ct);
            }
        }
        finally
        {
            watch.Cancel();
            try { await reader; } catch (OperationCanceledException) { }
        }
    }

    private void ShowOffset(ITaskRun run, PolarOffset o, double? scale)
    {
        var dir = $"{(o.XPx > 0 ? "←" : "→")} {(o.YPx > 0 ? "↓" : "↑")}";
        var values = new Dictionary<string, double> { ["xPx"] = o.XPx, ["yPx"] = o.YPx };
        var (arcmin, grade) = Evaluate(o, scale, run.Context);
        if (arcmin is { } a && grade is not null)
        {
            values["errorArcmin"] = a;
            values["xArcmin"] = Math.Abs(o.XPx) * scale!.Value / 60;
            values["yArcmin"] = Math.Abs(o.YPx) * scale.Value / 60;
            run.Readout("polar-offset", grade, $"극축 오차 {a:F1}′", PolarRules.Tone(a, PolarRules.ToleranceArcmin(run.Context)), values, live: true, observedAt: o.At);
        }
        else
            run.Readout("polar-offset", $"{dir} {Math.Abs(o.XPx):F0}·{Math.Abs(o.YPx):F0}px", "줄어들고 있는지 보세요 (환산 확인 전 — 방향과 픽셀만)", Tone.Busy, values, live: true, unverified: true, observedAt: o.At);
    }

    /// <summary>환산을 확인했으면 각도 오차와 등급, 아니면 (null, null) — CX-PREP-IMPL-03</summary>
    private (double? Arcmin, string? Grade) Evaluate(PolarOffset o, double? scale, PrepContext ctx)
    {
        if (!devices.OffsetUnitVerified || scale is not > 0) return (null, null);
        var arcmin = Math.Sqrt(o.XPx * o.XPx + o.YPx * o.YPx) * scale.Value / 60;
        return (arcmin, PolarRules.Grade(arcmin, PolarRules.ToleranceArcmin(ctx)));
    }

    private async Task GiveBackAsync(ITaskRun run, CancellationToken ct)
    {
        run.SubStep(3);
        run.Guide("돌려주기", "SharpCap을 닫고 가이드 카메라를 PHD2에 다시 연결합니다.");
        run.Live("none");
        while (true)
        {
            run.Status("가이드 카메라를 PHD2로 돌려주는 중입니다");
            var back = await devices.GiveBackGuideCameraAsync(ct);
            if (back.Ok) { run.Status(null); return; }
            run.Status($"가이드 카메라를 다시 연결하지 못했습니다. {back.Problem}".Trim(), Tone.Fail);
            await run.AskAsync([new("retry", "다시 시도", true)], ct);
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ct);
        if (!s.SharpCapClosed) return EndStateCheck.Fail("SharpCap이 아직 열려 있습니다");
        if (!s.GuideCameraOnPhd2) return EndStateCheck.Fail("가이드 카메라가 PHD2에 연결되어 있지 않습니다");
        if (s.Phd2Looping) return EndStateCheck.Fail("PHD2가 아직 사진을 찍고 있습니다");
        if (!s.NinaGuiderConnected) return EndStateCheck.Fail("N.I.N.A.가 PHD2에 연결되어 있지 않습니다");
        if (s.MountMoving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>극축 정렬 판정 (순수 함수). 허용 오차는 계획에서 — 계산 근거: 주 카메라 화각이 넓을수록 너그럽게</summary>
public static class PolarRules
{
    /// <summary>
    /// 이 계획에서 "충분해요"의 기준(′). 근거(임시): 한 장 노출 동안 극축 오차로 생기는 별 흐름이 주 카메라 한 픽셀을 넘지 않게 —
    /// 최악 흐름 ≈ 오차(rad) × 노출 동안 도는 각도(rad) → 오차′ ≈ 픽셀″ / (노출s × 15″/s × π/180) / 60. 너무 작거나 크지 않게 2~10′로 자른다.
    /// 맑은 날 실기로 다시 정한다.
    /// </summary>
    public static double ToleranceArcmin(PrepContext ctx)
    {
        var turnRad = ctx.ExposureSeconds * 15.0 / 3600 * Math.PI / 180;
        var arcmin = ctx.MainPixelScaleArcsec / Math.Max(turnRad, 1e-6) / 60;
        return Math.Clamp(arcmin, 2, 10);
    }

    public static string Grade(double arcmin, double tolerance) =>
        arcmin < 1 ? "완벽해요" : arcmin <= tolerance ? "충분해요" : arcmin <= tolerance * 3 ? "아쉬워요" : "멀어요";

    public static Tone Tone(double arcmin, double tolerance) =>
        arcmin <= tolerance ? Flow.Tone.Ok : arcmin <= tolerance * 3 ? Flow.Tone.Warn : Flow.Tone.Fail;
}
