using Astro.Server.Prepare.Flow;
using Astro.Server.Shoot;

namespace Astro.Server.Prepare.Tasks.Wrap;

// 마무리 묶음 (DESIGN.md "마무리", 시안 mockups/aa-shoot-wrap-v10.html): 플랫 → (뚜껑) → 플랫 다크·다크 → 장비 정리.
// 준비 묶음과 같은 러너·화면을 쓴다. 사용자가 할 일(패널 씌우기, 뚜껑 덮기)만 묻고 나머지는 묻지 않고 이어 간다(AutoNext).

/// <summary>마무리 장비. 실제: N.I.N.A.(적도의·촬영·연결 끊기) + 프로그램 닫기</summary>
public interface IWrapDevices
{
    /// <summary>플랫 패널을 씌우기 쉬운 위쪽(자오선, 고도 약 80°)으로</summary>
    Task<bool> PointUpAsync(CancellationToken ct);
    /// <summary>패널 밝기에 맞는 노출을 찾는다 (평균 밝기 목표 50%). 시도할 때마다 (노출 초, 평균 %)</summary>
    Task<FlatExposure> FindFlatExposureAsync(int iso, Action<double, double> trying, CancellationToken ct);
    /// <summary>한 장 찍어 저장 (FLAT · DARKFLAT · DARK)</summary>
    Task<bool> CaptureAsync(string imageType, double seconds, int iso, CancellationToken ct);
    /// <summary>홈으로 (Go Home만)</summary>
    Task<bool> HomeAsync(CancellationToken ct);
    Task<bool> TrackingOffAsync(CancellationToken ct);
    Task<bool> DisconnectAllAsync(CancellationToken ct);
    /// <summary>N.I.N.A.·PHD2 닫기</summary>
    Task<bool> CloseProgramsAsync(CancellationToken ct);
    Task<bool> StopAsync(CancellationToken ct);
}

/// <summary>TooShort = 패널이 너무 밝아 노출이 셔터 한계보다 짧음 (셔터 그림자), TooLong = 너무 어두움</summary>
public sealed record FlatExposure(bool Ok, double Seconds, double MeanPercent, bool TooShort = false, bool TooLong = false, string? Problem = null);

public sealed record FlatResult(bool Skipped, int Count, double ExposureSeconds, int Iso, double MeanPercent, DateTimeOffset At);
public sealed record DarkSet(int ExposureSeconds, int Iso, int Count);
public sealed record DarkResult(bool Skipped, int FlatDarks, IReadOnlyList<DarkSet> Sets, bool Homed, DateTimeOffset At);
/// <summary>UserConfirmed = 홈·추적 끄기를 확인하지 못해 사용자가 직접 확인했다고 누름</summary>
public sealed record PackResult(bool Homed, bool TrackingOff, bool Disconnected, bool Closed, bool UserConfirmed, DateTimeOffset At)
{
    /// <summary>전원을 꺼도 되는가: 적도의가 홈·추적 끔(또는 사용자 확인)이고 연결을 끊음</summary>
    public bool SafeToPowerOff => (Homed && TrackingOff || UserConfirmed) && Disconnected;
}

/// <summary>마무리 ① 플랫: 망원경을 위로 → 패널 씌우기(사용자) → 노출 찾기 → 30장. 끝나면 묻지 않고 다크(뚜껑 질문)로</summary>
public sealed class FlatTask(IWrapDevices devices) : IPrepTask
{
    public const int Count = 30;
    public string Id => "flat";
    public string Title => "플랫";
    public string StartLabel => "플랫 찍으러 가기";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("up", "위로"), new("panel", "패널"), new("exposure", "노출 찾기"), new("shoot", "촬영")];
    public bool AppliesTo(PrepContext ctx) => true;

    /// <summary>플랫 ISO = 그날 찍은 사진의 ISO (없으면 800)</summary>
    public static int IsoOf(PrepContext ctx) => ctx.Results.Get<NightShootResult>()?.Targets.FirstOrDefault()?.Iso ?? 800;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        var iso = IsoOf(ctx);
        while (true)
        {
            run.SubStep(0);
            run.Guide("망원경을 위로 보내고 있어요", "플랫 패널을 씌우기 쉽게 망원경을 하늘 위쪽으로 돌려요. 초점과 카메라 방향은 촬영 때 그대로 둬요.");
            run.Readout("count", $"{Count}장", "플랫 · 밝기에 맞춰 노출은 AA가 정해요", Tone.None);
            run.Status("적도의가 움직이는 중입니다");
            bool up;
            await using (await ctx.Mount.AcquireAsync(ct)) up = await devices.PointUpAsync(ct);
            if (up) break;
            run.Guide("망원경을 옮기지 못했습니다", "적도의 연결과 케이블을 확인한 뒤 다시 시도해 주세요. 손으로 패널을 씌울 수 있으면 그대로 진행해도 돼요.");
            run.Status("적도의가 움직이지 않았습니다", Tone.Fail);
            if (await run.AskAsync([new("retry", "다시 시도", true), new("here", "이 자리에서 진행")], ct) == "here") break;
        }

        run.SubStep(1);
        run.Guide("플랫 패널을 씌우고 켜 주세요", "망원경이 위를 보고 있어요. 패널을 망원경 앞에 씌우고 켠 뒤 눌러 주세요. 초점이나 카메라 방향이 바뀌면 플랫이 맞지 않으니 건드리지 마세요.");
        run.Status("적도의가 위를 보고 있어요", Tone.Ok);
        if (await run.AskAsync([new("panel", "씌우고 켰어요", true), new("skip", "보정 프레임 건너뛰기")], ct) == "skip")
            return new Completed(new FlatResult(true, 0, 0, iso, 0, ctx.Now()), "건너뜀", "보정 프레임을 건너뛰어요", "플랫·다크 없이 장비를 정리해요.") { AutoNext = true };

        FlatExposure exp;
        while (true)
        {
            run.SubStep(2);
            run.Guide("밝기에 맞는 노출을 찾고 있어요", "패널 빛으로 망원경·센서의 먼지와 주변 어두움을 기록해요. 사진의 평균 밝기가 절반쯤 되는 노출을 찾아요.");
            run.Status("노출을 바꿔 가며 밝기를 재는 중입니다");
            exp = await devices.FindFlatExposureAsync(iso, (s, m) =>
                run.Readout("flat-level", $"{m:F0}%", $"평균 밝기 · 노출 {s:0.###}초 · 목표 50% 근처", Tone.Busy, new Dictionary<string, double> { ["seconds"] = s, ["percent"] = m }), ct);
            if (exp.Ok) break;
            run.Guide(exp.TooShort ? "패널이 너무 밝아요" : exp.TooLong ? "패널이 너무 어두워요" : "노출을 찾지 못했습니다",
                exp.TooShort ? "노출이 아주 짧아져 셔터 그림자(밝기 얼룩)가 생길 수 있어요. 패널 밝기를 줄이거나 흰 천을 한 겹 더 씌운 뒤 다시 해 주세요."
                : exp.TooLong ? "패널 밝기를 올리거나 덮은 천을 줄인 뒤 다시 해 주세요."
                : "카메라 연결을 확인한 뒤 다시 해 주세요.");
            run.Status(exp.Problem ?? "알맞은 노출을 찾지 못했습니다", Tone.Fail);
            if (await run.AskAsync([new("retry", "다시 찾기", true), new("skip", "보정 프레임 건너뛰기")], ct) == "skip")
                return new Completed(new FlatResult(true, 0, 0, iso, 0, ctx.Now()), "건너뜀", "보정 프레임을 건너뛰어요", "플랫·다크 없이 장비를 정리해요.") { AutoNext = true };
        }

        run.SubStep(3);
        run.Guide("플랫을 찍고 있어요", "패널 빛으로 망원경·센서의 먼지와 주변 어두움을 기록해요. 스태킹 때 이걸로 사진의 얼룩을 지워요.");
        var taken = 0;
        var fails = 0;
        while (taken < Count)
        {
            run.Readout("count", $"{taken} / {Count}장", $"플랫 · 노출 {exp.Seconds:0.###}초 · 평균 밝기 {exp.MeanPercent:F0}%", Tone.Busy, live: true);
            run.Status("플랫 촬영 중입니다");
            if (await devices.CaptureAsync("FLAT", exp.Seconds, iso, ct)) { taken++; fails = 0; continue; }
            if (++fails < 3) continue;
            run.Status("플랫을 찍지 못했습니다", Tone.Fail);
            await run.AskAsync([new("retry", "다시 시도", true)], ct);
            fails = 0;
        }
        run.Readout("count", $"{Count}장", $"플랫 · 노출 {exp.Seconds:0.###}초 · 평균 밝기 {exp.MeanPercent:F0}%", Tone.Ok);
        run.Status(null);
        return new Completed(new FlatResult(false, Count, exp.Seconds, iso, exp.MeanPercent, ctx.Now()), $"{Count}장 · {exp.Seconds:0.###}초",
            "플랫을 찍었어요", "\"플랫\" 폴더에 담았어요. 이어서 플랫 다크와 다크를 찍어요.") { AutoNext = true };
    }

    public Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct) => Task.FromResult(EndStateCheck.Pass);
    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>
/// 마무리 ② 다크: 뚜껑 덮기(사용자, 다크 장수 고르기 — 기본 20, 5장씩 5~40) → 적도의 홈(동시에) → 플랫 다크 30장 → 다크.
/// 다크는 그날 실제로 찍은 노출·ISO마다. 촬영 중 "여기까지만 찍기"로 찍은 만큼만 쓰고 끝낼 수 있다.
/// </summary>
public sealed class DarkTask(IWrapDevices devices) : IPrepTask
{
    public const int DefaultCount = 20, MinCount = 5, MaxCount = 40, StepCount = 5, FlatDarks = 30, MaxFails = 3;

    /// <summary>연속으로 찍지 못함 (CX-SHOOT-08): 이유와 함께 다시 시도 · 지금까지만 쓰기 · 건너뛰기</summary>
    private static async Task<string> CaptureFailedAsync(ITaskRun run, string what, int taken, CancellationToken ct)
    {
        run.Guide($"{what}를 찍지 못했습니다", "카메라 연결이나 저장 공간을 확인한 뒤 다시 시도해 주세요. 지금까지 찍은 것만 쓰거나 건너뛸 수도 있어요.");
        run.Status($"{what} {MaxFails}장을 이어서 찍지 못했습니다", Tone.Fail);
        return await run.AskAsync([new("retry", "다시 시도", true), new("keep", $"지금까지만 쓰기 ({taken}장)"), new("skip", "다크 건너뛰기")], ct);
    }
    private Task<bool>? _home;

    public string Id => "dark";
    public string Title => "다크";
    public string StartLabel => "다크 찍기";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("cap", "뚜껑"), new("flatdark", "플랫 다크"), new("dark", "다크")];
    public bool AppliesTo(PrepContext ctx) => true;

    /// <summary>다크를 찍을 노출·ISO들: 그날 찍은 사진 기준 (없으면 기본 노출)</summary>
    public static IReadOnlyList<(int Exposure, int Iso)> SetsOf(PrepContext ctx)
    {
        var sets = ctx.Results.Get<NightShootResult>()?.Targets.Where(t => t.Good + t.Excluded > 0)
            .Select(t => (t.ExposureSeconds, t.Iso)).Distinct().ToList() ?? [];
        return sets.Count > 0 ? sets : [(ctx.ExposureSeconds, FlatTask.IsoOf(ctx))];
    }

    public static string Hm(double seconds)
    {
        var m = (int)Math.Round(seconds / 60);
        var h = m / 60;
        return h > 0 && m % 60 == 0 ? $"{h}시간" : h > 0 ? $"{h}시간 {m % 60}분" : $"{m}분";
    }

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        var flat = ctx.Results.Get<FlatResult>();
        if (flat is { Skipped: true })
            return new Completed(new DarkResult(true, 0, [], false, ctx.Now()), "건너뜀", "다크를 건너뛰어요", "장비를 정리해요.") { AutoNext = true };
        var sets = SetsOf(ctx);
        var count = DefaultCount;

        // 뚜껑 + 장수 고르기
        while (true)
        {
            run.SubStep(0);
            run.Guide("패널을 끄고 렌즈 뚜껑을 덮어 주세요",
                "플랫은 다 찍었어요. 빛이 들지 않게 덮으면 플랫 다크와 다크를 이어서 찍어요. 그동안 적도의는 홈으로 보내요. 다크를 찍는 동안 다른 짐을 정리하셔도 돼요.");
            var total = sets.Sum(s => s.Exposure) * count;
            var expText = string.Join(" · ", sets.Select(s => $"{s.Exposure}초 ISO {s.Iso}"));
            run.Readout("dark-count", $"{count}", $"다크 · 오늘 찍은 사진과 같은 {expText} × {count}장 ≈ {Hm(total)} (플랫 다크 {FlatDarks}장은 약 1분)", Tone.None,
                new Dictionary<string, double> { ["count"] = count, ["min"] = MinCount, ["max"] = MaxCount, ["seconds"] = total });
            run.Status(flat is { Count: > 0 } f ? $"플랫 {f.Count}장을 찍었어요 · 노출 {f.ExposureSeconds:0.###}초 · \"플랫\" 폴더" : null, Tone.Ok);
            var c = await run.AskAsync([new("covered", "덮었어요", true), new("less", "줄이기"), new("more", "늘리기"), new("skip", "다크 건너뛰기")], ct);
            if (c == "less") { count = Math.Max(MinCount, count - StepCount); continue; }
            if (c == "more") { count = Math.Min(MaxCount, count + StepCount); continue; }
            if (c == "skip") return new Completed(new DarkResult(true, 0, [], false, ctx.Now()), "건너뜀", "다크를 건너뛰어요", "장비를 정리해요.") { AutoNext = true };
            break;
        }

        // 뚜껑을 덮었으니 적도의는 홈으로 (다크와 함께)
        _home = Task.Run(async () =>
        {
            await using (await ctx.Mount.AcquireAsync(ct)) return await devices.HomeAsync(ct);
        }, ct);
        string Homing() => _home.IsCompletedSuccessfully ? (_home.Result ? "적도의는 홈에 두었어요 · " : "적도의를 홈으로 보내지 못했어요 · ") : "적도의를 홈으로 보내는 중 · ";

        run.SubStep(1);
        run.Guide("플랫 다크를 찍고 있어요", "플랫과 같은 노출로 빛 없이 찍어 센서 자체의 신호를 기록해요.");
        var flatExp = flat?.ExposureSeconds ?? 1;
        var flatIso = flat?.Iso ?? FlatTask.IsoOf(ctx);
        var flatDarks = 0;
        var fails = 0;
        while (flatDarks < FlatDarks)
        {
            run.Readout("count", $"{flatDarks} / {FlatDarks}장", $"플랫 다크 · 노출 {flatExp:0.###}초", Tone.Busy, live: true);
            run.Status(Homing() + "다크 촬영 중에는 다른 짐을 정리하셔도 돼요");
            if (await devices.CaptureAsync("DARKFLAT", flatExp, flatIso, ct)) { flatDarks++; fails = 0; continue; }
            if (++fails < MaxFails) continue;
            fails = 0;
            var c = await CaptureFailedAsync(run, "플랫 다크", flatDarks, ct);
            if (c == "skip") return new Completed(new DarkResult(true, flatDarks, [], await _home, ctx.Now()), "건너뜀", "다크를 건너뛰어요", "장비를 정리해요.") { AutoNext = true };
            if (c == "keep") break;
        }

        run.SubStep(2);
        run.Guide("다크를 찍고 있어요", "촬영과 같은 노출·ISO로 빛 없이 찍어 센서의 열 잡음을 기록해요. 촬영 직후라 센서 온도가 비슷해 잘 맞아요.");
        var done = new List<DarkSet>();
        var stop = false;
        foreach (var (exposure, iso) in sets)
        {
            var taken = 0;
            while (taken < count && !stop)
            {
                var left = (count - taken) * exposure + sets.SkipWhile(s => s != (exposure, iso)).Skip(1).Sum(s => s.Exposure * count);
                run.Readout("count", $"{taken} / {count}장", $"다크 · {exposure}초 · ISO {iso} · 남은 시간 약 {Hm(left)}", Tone.Busy, live: true);
                run.Status(Homing() + "다크 촬영 중에는 다른 짐을 정리하셔도 돼요");
                var capture = devices.CaptureAsync("DARK", exposure, iso, ct);
                // 서둘러 떠나야 할 때: 지금까지 찍은 다크만 쓴다 (지금 장은 끝까지)
                using var askCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task<string>? ask = taken + done.Sum(d => d.Count) > 0 ? run.AskAsync([new("stop-here", $"여기까지만 찍기 ({taken + done.Sum(d => d.Count)}장)")], askCts.Token) : null;
                if (ask is not null && await Task.WhenAny(capture, ask) == ask && ask.IsCompletedSuccessfully) stop = true;
                askCts.Cancel();
                try { if (ask is not null) await ask; } catch (OperationCanceledException) { }
                if (await capture) { taken++; fails = 0; continue; }
                if (++fails < MaxFails) continue;
                fails = 0;
                var c = await CaptureFailedAsync(run, "다크", taken + done.Sum(d => d.Count), ct);
                if (c == "skip") return new Completed(new DarkResult(true, flatDarks, done, await _home, ctx.Now()), "건너뜀", "다크를 건너뛰어요", "장비를 정리해요.") { AutoNext = true };
                if (c == "keep") stop = true;
            }
            done.Add(new DarkSet(exposure, iso, taken));
            if (stop) break;
        }
        var homed = await _home;
        var n = done.Sum(d => d.Count);
        run.Readout("count", $"{n}장", $"플랫 다크 {flatDarks}장 · 다크 {n}장 · \"다크\" 폴더", Tone.Ok);
        run.Status(null);
        return new Completed(new DarkResult(false, flatDarks, done, homed, ctx.Now()), $"플랫 다크 {flatDarks} · 다크 {n}장",
            "다크를 찍었어요", "이제 장비를 정리해요. 뚜껑은 그대로 두셔도 돼요.") { AutoNext = true };
    }

    public Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct) => Task.FromResult(EndStateCheck.Pass);
    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}

/// <summary>마무리 ③ 장비 정리: 적도의 홈·추적 끔 → 장비 연결 끊기 → N.I.N.A.·PHD2 닫기 (묻지 않음). 끝나면 오늘 밤 요약</summary>
public sealed class PackTask(IWrapDevices devices) : IPrepTask
{
    public string Id => "pack";
    public string Title => "장비 정리";
    public string StartLabel => "장비 정리";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("home", "홈 · 추적 끔"), new("disconnect", "연결 끊기"), new("close", "프로그램 닫기")];
    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        var ctx = run.Context;
        run.Guide("장비를 정리하고 있어요", "적도의를 홈에 두고 추적을 끈 뒤, 장비 연결을 끊고 프로그램을 닫아요. 전원은 이 다음에 끄시면 돼요.");
        run.SubStep(0);
        run.Readout("pack", "1 / 3", "적도의 홈 · 추적 끔", Tone.Busy);
        run.Status("적도의를 정리하는 중입니다");
        bool homed, trackingOff, confirmed = false, first = true;
        while (true)
        {
            await using (await ctx.Mount.AcquireAsync(ct))
            {
                homed = first && ctx.Results.Get<DarkResult>() is { Homed: true } || await devices.HomeAsync(ct);
                // 홈을 확인하지 못했으면 움직임부터 멈춘다 (홈 시간 초과가 정지를 뜻하지 않음)
                if (!homed) await devices.StopAsync(ct);
                trackingOff = await devices.TrackingOffAsync(ct);
            }
            first = false;
            if (homed && trackingOff) break;
            // 확인 전에는 연결 끊기·프로그램 닫기·전원 안내를 하지 않는다 (CX-SHOOT-03)
            run.Guide("적도의를 정리하지 못했습니다", homed
                ? "추적을 끄지 못했어요. N.I.N.A.에서 추적이 꺼졌는지 확인해 주세요."
                : "홈 위치에 도착한 것을 확인하지 못해 적도의를 멈췄어요. 적도의가 멈춰 있는지, 케이블이 걸리지 않았는지 확인해 주세요.");
            run.Readout("pack", "1 / 3", $"{(homed ? "홈" : "홈 확인 못 함")} · {(trackingOff ? "추적 끔" : "추적 끄기 확인 못 함")}", Tone.Fail);
            run.Status("장비 연결을 끊기 전에 적도의를 확인해야 해요", Tone.Fail);
            if (await run.AskAsync([new("retry", "다시 시도", true), new("confirmed", "직접 확인했어요")], ct) == "confirmed") { confirmed = true; break; }
            run.Guide("장비를 정리하고 있어요", "적도의를 홈에 두고 추적을 끈 뒤, 장비 연결을 끊고 프로그램을 닫아요. 전원은 이 다음에 끄시면 돼요.");
            run.Readout("pack", "1 / 3", "적도의 홈 · 추적 끔", Tone.Busy);
            run.Status("적도의를 정리하는 중입니다");
        }
        run.SubStep(1);
        run.Readout("pack", "2 / 3", "장비 연결 끊기", Tone.Busy);
        run.Status("장비 연결을 끊는 중입니다");
        var disconnected = await devices.DisconnectAllAsync(ct);
        run.SubStep(2);
        run.Readout("pack", "3 / 3", "N.I.N.A.·PHD2 닫기", Tone.Busy);
        run.Status("프로그램을 닫는 중입니다");
        var closed = await devices.CloseProgramsAsync(ct);
        var result = new PackResult(homed, trackingOff, disconnected, closed, confirmed, ctx.Now());
        var line = $"{(homed ? "홈" : confirmed ? "홈 (직접 확인)" : "홈 확인 못 함")} · {(disconnected ? "연결 끊음" : "연결 일부 남음")} · {(closed ? "프로그램 닫음" : "프로그램 열려 있음")}";
        run.Readout("pack", "끝", line, result.SafeToPowerOff && closed ? Tone.Ok : Tone.Warn);
        run.Status(null);
        return new Completed(result, line, "장비를 정리했어요", "오늘 밤 요약을 보여 드릴게요.") { AutoNext = true };
    }

    public Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct) => Task.FromResult(EndStateCheck.Pass);
    public Task<bool> StopAsync(CancellationToken ct) => devices.StopAsync(ct);
}
