using Astro.Server.Prepare.Flow;

namespace Astro.Server.Prepare.Tasks.Slew;

/// <summary>③ 대상 이동 장비. 실제: N.I.N.A. slew(RA는 도 단위)·abort, 고도는 계획 좌표와 관측지로 계산</summary>
public interface ISlewDevices
{
    /// <summary>대상의 지금 고도(도)</summary>
    double TargetAltitude(PrepContext ctx);
    /// <summary>30° 위로 올라올 때까지 남은 분 (오늘 밤 안 올라오면 null)</summary>
    int? MinutesUntilUsable(PrepContext ctx);
    /// <summary>기다리는 동안 다시 확인하는 간격</summary>
    Task WaitTickAsync(CancellationToken ct);
    /// <summary>대상으로 이동. 남은 거리(도)를 알린다. 도착 오차(도)를 돌려준다</summary>
    Task<SlewMove> SlewToTargetAsync(double raDeg, double decDeg, Action<double> remainingDeg, CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
    Task<SlewEndState> ReadEndStateAsync(PrepContext ctx, CancellationToken ct);
}

public sealed record SlewMove(bool Ok, double ArrivalErrorDeg, string? Problem = null);
public sealed record SlewEndState(double DistanceToTargetDeg, bool Moving, bool Tracking);

/// <summary>
/// ③ 대상 이동 (DESIGN.md ③): ② 끝 버튼 "대상으로 이동"이 이동 승인. 낮은 대상은 계획 단계에서 알렸고 여기선
/// "기다리기"(남은 시간 + 때가 되면 알림) → "기다리기 끝" → "이동". 30° 아래면 이동을 막는다. 이동 중 "멈춤".
/// </summary>
public sealed class SlewTask(ISlewDevices devices) : IPrepTask
{
    public const double UsableAltitude = 30;
    public const double ArrivalLimitDeg = 2;

    public string Id => "slew";
    public string Title => "대상 이동";
    public string StartLabel => "대상으로 이동";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("check", "이동 조건 확인"), new("move", "대상으로 이동"), new("arrive", "도착 확인")];

    public bool AppliesTo(PrepContext ctx) => true;

    /// <summary>이동이 안 될 때 "지금 위치를 목표로" — 핸드 컨트롤러·성도 앱으로 이미 맞춘 경우 (2026-10-08 사용자 결정). 센터링도 건너뜀</summary>
    private static readonly PrepAction AcceptHere = new("here", "지금 위치를 목표로");
    private bool _acceptHere;

    private Completed Accepted(PrepContext ctx) =>
        new(new SlewResult(0, devices.TargetAltitude(ctx), ctx.Now(), AcceptedHere: true), "지금 위치를 목표로", "지금 위치에서 찍어요",
            "적도의를 옮기지 않고 지금 가리키는 곳을 대상으로 봐요. 가운데 맞추기(센터링)도 건너뛰니, 시험 사진에서 구도를 확인해 주세요.") { AutoNext = true };

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        _acceptHere = false;
        run.Live("sky-map");
        while (true)
        {
            run.SubStep(0);
            await WaitUntilUsableAsync(run, ct);
            var moved = await MoveAsync(run, ct);
            if (_acceptHere) return Accepted(ctx);
            if (moved is null) continue; // 멈춤 → 다시 이동을 누르면 처음부터(고도 확인 포함)
            run.SubStep(2);
            if (moved.ArrivalErrorDeg > ArrivalLimitDeg)
            {
                run.Status($"목표에서 {moved.ArrivalErrorDeg:F1}° 벗어난 곳에 멈췄습니다. 다시 이동해 주세요", Tone.Fail);
                if (await run.AskAsync([new("move", "다시 이동", true), AcceptHere], ct) == "here") return Accepted(ctx);
                continue;
            }
            var alt = devices.TargetAltitude(ctx);
            run.Readout("distance", $"{moved.ArrivalErrorDeg:F1}°", "대상까지 남은 거리 · 목표 2° 안", Tone.Ok,
                new Dictionary<string, double> { ["arrivalDeg"] = moved.ArrivalErrorDeg, ["altitudeDeg"] = alt });
            run.Status(null);
            return new Completed(new SlewResult(moved.ArrivalErrorDeg, alt, ctx.Now()), $"고도 {alt:F0}° · 도착 오차 {moved.ArrivalErrorDeg:F1}°",
                $"{ctx.TargetName}에 도착했습니다", "목표 근처에 도착했습니다. 다음은 사진을 찍어 대상을 화면 가운데로 맞추고, 그 사진으로 구도를 확인합니다.");
        }
    }

    /// <summary>대상이 30° 위가 될 때까지: 기다리기 / 이동(막음) / 기다리기 끝</summary>
    private async Task WaitUntilUsableAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        while (!ctx.IgnoreAltitude && devices.TargetAltitude(ctx) < UsableAltitude)
        {
            var minutes = devices.MinutesUntilUsable(ctx);
            var when = minutes is { } m ? $"약 {m}분 뒤 30° 위로 올라옵니다" : "오늘 밤 30° 위로 올라오지 않습니다";
            run.Guide($"{ctx.TargetName}{Core.Josa.Pick(ctx.TargetName, "은", "는")} 아직 낮습니다", $"{when}. 계획 단계에서 알려 드린 대기입니다.");
            ShowAltitude(run, ctx);
            run.Status("대상이 30° 아래에 있습니다", Tone.Warn);
            var choice = await run.AskAsync([new("wait", "기다리기", true), new("move", "이동")], ct);
            if (choice == "move")
            {
                run.Status($"아직 떠오르지 않아 이동할 수 없습니다{(minutes is { } mm ? $" (약 {mm}분 뒤 가능)" : "")}", Tone.Fail);
                await run.AskAsync([new("wait", "기다리기", true)], ct);
            }
            await WaitAsync(run, ct);
        }
    }

    private async Task WaitAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        run.Guide("기다리는 중입니다", "때가 되면 알려 드립니다. 그동안 장비는 그대로 둡니다.");
        using var asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var endWait = run.AskAsync([new("end-wait", "기다리기 끝", true)], asking.Token);
        while (!endWait.IsCompleted)
        {
            ShowAltitude(run, ctx);
            if (devices.TargetAltitude(ctx) >= UsableAltitude)
            {
                // 때가 됨: 알림(나중에 휴대폰 진동)
                run.Status($"{ctx.TargetName}이 30° 위로 올라왔습니다 — 이동할 수 있어요", Tone.Ok);
                asking.Cancel();
                try { await endWait; } catch (OperationCanceledException) { }
                await run.AskAsync([new("move", "이동", true)], ct);
                return;
            }
            var m = devices.MinutesUntilUsable(ctx);
            run.Status(m is { } mm ? $"대상이 올라오기를 기다리는 중입니다 · 약 {mm}분 남음" : "대상이 올라오기를 기다리는 중입니다");
            await Task.WhenAny(endWait, devices.WaitTickAsync(ct));
        }
        await endWait; // 사용자가 일찍 "기다리기 끝" — 바깥 반복이 고도를 다시 본다
    }

    private void ShowAltitude(ITaskRun run, PrepContext ctx)
    {
        var alt = devices.TargetAltitude(ctx);
        run.Readout("altitude", $"{alt:F0}°", $"{ctx.Plan.TargetName} 지금 고도 · 30° 위로 올라오면 이동", alt >= UsableAltitude ? Tone.Ok : Tone.Warn,
            new Dictionary<string, double> { ["altitudeDeg"] = alt });
    }

    /// <summary>이동. 멈춤을 누르면 정지를 확인하고 null</summary>
    private async Task<SlewMove?> MoveAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        while (true)
        {
            run.SubStep(1);
            run.Guide($"{ctx.TargetName}로 이동하고 있습니다", "적도의가 대상 쪽으로 돌고 있습니다. 케이블이 걸리면 멈춤을 누르세요.");
            run.Status("이동 중입니다");
            using var moving = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stop = run.AskAsync([new("stop", "멈춤", true)], asking.Token);
            Task<SlewMove> slew;
            var stopPressed = false;
            var stopped = false;
            await using (await ctx.Mount.AcquireAsync(ct))
            {
                slew = devices.SlewToTargetAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees,
                    d => run.Readout("distance", $"{d:F1}°", "대상까지 남은 거리 (적도의 위치)", Tone.Busy, new Dictionary<string, double> { ["remainingDeg"] = d }, live: true), moving.Token);
                await Task.WhenAny(slew, stop);
                if (stop.IsCompletedSuccessfully)
                {
                    stopPressed = true;
                    moving.Cancel();
                    // 정지 명령은 이동 명령이 끝나기를 기다리지 않고 바로 (어댑터가 취소에 늦게 응답해도 멈추게, CX-PREP-CODE-02)
                    stopped = await TryStopAsync();
                    try { await slew.WaitAsync(StopWait); } catch (Exception e) when (e is OperationCanceledException or TimeoutException) { }
                }
            }
            if (stopPressed)
            {
                run.Guide("이동을 멈췄습니다", "케이블 등을 확인한 뒤 다시 이동하세요.");
                // 멈춘 것을 확인하기 전에는 다시 이동할 수 없다 (CX-PREP-CODE-02)
                while (!stopped)
                {
                    run.Status("적도의가 멈췄는지 확인하지 못했습니다. 적도의를 직접 확인한 뒤 장비 상태 다시 확인을 눌러 주세요", Tone.Fail);
                    await run.AskAsync([new("recheck-stop", "장비 상태 다시 확인", true)], ct);
                    await using (await ctx.Mount.AcquireAsync(ct))
                        stopped = await TryStopAsync();
                }
                run.Status("적도의가 멈춰 있습니다", Tone.Warn);
                await run.AskAsync([new("move", "다시 이동", true)], ct);
                return null;
            }
            asking.Cancel();
            try { await stop; } catch (OperationCanceledException) { }
            var result = await slew;
            if (result.Ok) return result;
            run.Guide("이동을 마치지 못했습니다", "적도의가 응답하지 않습니다. 적도의 전원과 케이블을 확인해 주세요.");
            run.Status(result.Problem ?? "이동하지 못했습니다", Tone.Fail);
            if (await run.AskAsync([new("move", "다시 시도", true), AcceptHere], ct) == "here") { _acceptHere = true; return null; }
        }
    }

    public async Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        var s = await devices.ReadEndStateAsync(ctx, ct);
        if (s.Moving) return EndStateCheck.Fail("적도의가 아직 움직이고 있습니다");
        if (!s.Tracking) return EndStateCheck.Fail("적도의 추적이 꺼져 있습니다");
        if (ctx.Results.Get<SlewResult>() is not { AcceptedHere: true } && s.DistanceToTargetDeg > ArrivalLimitDeg) return EndStateCheck.Fail($"적도의가 목표에서 {s.DistanceToTargetDeg:F1}° 벗어나 있습니다");
        return EndStateCheck.Pass;
    }

    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(15);

    /// <summary>정지 명령 + 확인. 예외·시간 초과는 확인 못 함(false)</summary>
    private async Task<bool> TryStopAsync()
    {
        using var cts = new CancellationTokenSource(StopWait);
        try { return await devices.StopAsync(cts.Token); }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }
}
