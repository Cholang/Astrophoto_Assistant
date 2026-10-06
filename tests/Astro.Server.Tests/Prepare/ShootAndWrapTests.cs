using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Wrap;
using Astro.Server.Shoot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>촬영(사진 등급·끝 조건·자동 사건)과 마무리(플랫 → 다크 → 장비 정리) — 모의 장비</summary>
public class ShootAndWrapTests
{
    private static readonly GradeBaseline Base = new(2.1, 45, 1100, 2.41);

    [Fact]
    public void 등급_A플러스부터_F까지()
    {
        Assert.Equal("A+", ShotGrader.Judge(new FrameStats(2.15, 46, 1100, .3, .5), Base).Letter);
        Assert.Equal("A", ShotGrader.Judge(new FrameStats(2.3, 46, 1100, .3, .5), Base).Letter);
        Assert.Equal("B", ShotGrader.Judge(new FrameStats(2.6, 46, 1100, .3, .5), Base).Letter);
        Assert.Equal("C", ShotGrader.Judge(new FrameStats(2.9, 46, 1100, .3, .5), Base).Letter);
        Assert.Equal("C", ShotGrader.Judge(new FrameStats(2.2, 46, 1800, .3, .5), Base).Letter); // 배경이 밝음
        // 실패: 별 흐름(가이딩) · 별 흐름(모양) · 구름 · 잡광 · 초점
        Assert.Equal("F", ShotGrader.Judge(new FrameStats(2.2, 46, 1100, .3, 6.5), Base).Letter);
        Assert.Equal("F", ShotGrader.Judge(new FrameStats(2.2, 46, 1100, .85, .5), Base).Letter);
        Assert.Equal("F", ShotGrader.Judge(new FrameStats(2.2, 12, 1100, .3, .5), Base).Letter);
        Assert.Equal("F", ShotGrader.Judge(new FrameStats(2.2, 46, 3000, .3, .5), Base).Letter);
        Assert.Equal("F", ShotGrader.Judge(new FrameStats(4.0, 46, 1100, .3, .5), Base).Letter);
        // 기준(별 수·배경)이 아직 없으면 그것으로는 판정하지 않는다
        Assert.Equal("A+", ShotGrader.Judge(new FrameStats(2.15, 12, 3000, null, .5), Base with { Stars = null, Mean = null }).Letter);
        Assert.All(ShotGrader.Letters, l => Assert.True(ShotGrader.Criteria.ContainsKey(l)));
        Assert.Contains("제외 폴더", ShotGrader.Criteria["F"]);
    }

    private sealed record Rig(ShootSession Session, SimFaults Faults, PrepContext Ctx);

    private static Rig NewShoot(int frames)
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        var session = new ShootSession(new SimulatedShootDevices(sim, faults), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var results = new PrepResults();
        results.Set(new FocusResult(false, 12340, 2.1, 12.4, DateTimeOffset.Now));
        var ctx = Harness.TargetContext(results, new InMemoryPrepMemory(), Harness.Plan() with { EstimatedFrames = frames });
        return new Rig(session, faults, ctx);
    }

    private static async Task<ShootView> UntilEnded(ShootSession s)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var wait = s.NextChangeAsync();
            var v = s.View();
            if (v.Mode == ShootMode.Ended) return v;
            await Task.WhenAny(wait, Task.Delay(200));
        }
        throw new TimeoutException("촬영이 끝나지 않음: " + s.View().Mode);
    }

    [Fact]
    public async Task 계획한_장수를_다_찍으면_끝나고_밤_기록에_남는다()
    {
        var r = NewShoot(5);
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal("done", v.Ended);
        Assert.Equal(5, v.Good);
        Assert.Equal(5, v.Tally.Values.Sum());
        var night = r.Ctx.Results.Get<NightShootResult>()!.Targets.Single();
        Assert.Equal(5, night.Good);
        Assert.Equal(120, night.ExposureSeconds);
    }

    [Fact]
    public async Task 실패한_사진은_F로_제외되고_쓸_사진만_센다()
    {
        var r = NewShoot(4);
        r.Faults.Arm("shoot.trail");
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal(1, v.Excluded);
        Assert.Equal(1, v.Tally["F"]);
        Assert.Equal(4, v.Good); // 제외한 만큼 더 찍는다
    }

    [Fact]
    public async Task 자오선_반전과_초점_다시와_구름은_묻지_않고_이어서_찍는다()
    {
        var r = NewShoot(6);
        r.Faults.Arm("shoot.flip");
        r.Faults.Arm("shoot.temp");
        r.Faults.Arm("shoot.cloud");
        r.Session.Start(r.Ctx);
        await UntilEnded(r.Session);
        var seen = r.Session.ModeHistory;
        Assert.Contains(ShootMode.Flip, seen);
        Assert.Contains(ShootMode.Focus, seen);
        Assert.Contains(ShootMode.Paused, seen);
        Assert.Equal("done", r.Session.View().Ended);
    }

    [Fact]
    public async Task 대상이_낮아지면_끝나고_중단하면_지금_사진까지_찍고_끝난다()
    {
        var low = NewShoot(50);
        low.Faults.Arm("shoot.low");
        low.Session.Start(low.Ctx);
        Assert.Equal("low", (await UntilEnded(low.Session)).Ended);

        var stop = NewShoot(50);
        stop.Session.Start(stop.Ctx);
        Assert.Null(stop.Session.Stop());
        var v = await UntilEnded(stop.Session);
        Assert.Equal("user", v.Ended);
        Assert.NotNull(stop.Session.Stop()); // 끝난 뒤에는 받지 않음
    }

    // ── 가이드 별 잃음 (원인 가르기 · 대응) ─────────

    private static GuideSignals Sig(bool guiding = true, bool tracking = true, bool connected = true, double[]? snr = null, double[]? hfd = null,
        double? jump = null, double? dew = null, double? stars = null, double? mean = null) =>
        new(new GuideRaw(connected, guiding, tracking, snr ?? [30, 31, 29], hfd ?? [2.2, 2.2, 2.2], jump, dew), stars, mean, 30, 2.2);

    [Fact]
    public void 가이드_별_잃은_원인_가르기()
    {
        Assert.Equal(GuideLoss.None, GuideWatch.Classify(Sig()));
        Assert.Equal(GuideLoss.Disconnected, GuideWatch.Classify(Sig(connected: false)));
        Assert.Equal(GuideLoss.MountStopped, GuideWatch.Classify(Sig(tracking: false, guiding: false)));
        Assert.Equal(GuideLoss.Jump, GuideWatch.Classify(Sig(guiding: false, jump: 9)));
        Assert.Equal(GuideLoss.Light, GuideWatch.Classify(Sig(guiding: false, mean: 2.5)));
        Assert.Equal(GuideLoss.Light, GuideWatch.Classify(Sig(guiding: false, snr: [30, 31, 29])));          // 갑자기
        Assert.Equal(GuideLoss.Cloud, GuideWatch.Classify(Sig(guiding: false, snr: [20, 12, 6, 3])));       // 서서히
        var faint = Enumerable.Repeat(10.0, 12).ToArray();
        Assert.Equal(GuideLoss.Dew, GuideWatch.Classify(Sig(snr: faint, dew: 1.2)));
        Assert.Equal(GuideLoss.GuideFocus, GuideWatch.Classify(Sig(snr: faint, hfd: Enumerable.Repeat(3.2, 12).ToArray(), dew: 6)));
        Assert.Equal(GuideLoss.Cloud, GuideWatch.Classify(Sig(snr: faint, stars: 0.4)));                   // 옅은 구름
        Assert.Equal(GuideLoss.Faint, GuideWatch.Classify(Sig(snr: faint)));
        Assert.InRange(GuideWatch.DewPoint(10, 90), 8.2, 8.6);
    }

    [Theory]
    [InlineData("shoot.light", "light")]
    [InlineData("shoot.cloud", "cloud")]
    [InlineData("shoot.wind", "jump")]
    [InlineData("shoot.guider", "guider")]
    public async Task 가이드_별을_잃으면_원인별로_멈췄다_이어서_찍는다(string fault, string pause)
    {
        var r = NewShoot(4);
        r.Faults.Arm(fault);
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal("done", v.Ended);
        Assert.Contains(ShootMode.Paused, r.Session.ModeHistory);
        var paused = r.Ctx.Results.Get<NightShootResult>()!.Targets.Single().PausedMinutes!;
        Assert.True(paused.ContainsKey(pause), string.Join(",", paused.Keys) + " | " + string.Join(",", r.Session.ModeHistory));
    }

    [Fact]
    public async Task 적도의가_멈추면_기다리지_않고_끝낸다()
    {
        var r = NewShoot(10);
        r.Faults.Arm("shoot.mount");
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal("mount", v.Ended);
        Assert.Equal(0, v.Good);
    }

    // ── Codex 리뷰 14절 (CX-SHOOT-01~09) ─────────

    [Fact]
    public async Task CX01_가이딩_정지를_확인하지_못하면_다음으로_못_가고_다시_확인하면_풀린다()
    {
        var r = NewShoot(2);
        r.Faults.Arm("shoot.stopfail");
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.False(v.GuideStopped);
        Assert.NotNull(r.Session.BlocksNext());
        Assert.Null(await r.Session.RecheckStopAsync());
        Assert.True(r.Session.View().GuideStopped);
        Assert.Null(r.Session.BlocksNext());
    }

    [Fact]
    public async Task CX02_반전_자체가_실패하면_더_찍지_않고_끝낸다()
    {
        var r = NewShoot(20);
        r.Faults.Arm("shoot.flipfail");
        r.Faults.Arm("shoot.flip");
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal("flip", v.Ended);
        Assert.Equal(0, v.Good + v.Excluded);
    }

    [Fact]
    public async Task CX06_PHD2가_가이딩_중이어도_적도의가_멈추면_끝낸다()
    {
        // 모의 "shoot.mount"는 가이딩 상태와 별개로 추적만 꺼진다 — 분류 단위로도 확인
        Assert.Equal(GuideLoss.MountStopped, GuideWatch.Classify(Sig(guiding: true, tracking: false)));
        var r = NewShoot(10);
        r.Faults.Arm("shoot.mount");
        r.Session.Start(r.Ctx);
        Assert.Equal("mount", (await UntilEnded(r.Session)).Ended);
    }

    [Fact]
    public async Task CX07_가이더_없는_구성은_가이드_문제_없이_끝까지_찍는다()
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        faults.Arm("shoot.guider"); // 가이더가 없으니 영향 없어야 함
        var session = new ShootSession(new SimulatedShootDevices(sim, faults), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var results = new PrepResults();
        results.Set(new FocusResult(false, 12340, 2.1, 12.4, DateTimeOffset.Now));
        var ctx = Harness.TargetContext(results, new InMemoryPrepMemory(), Harness.Plan() with { EstimatedFrames = 4 }, hasGuider: false);
        session.Start(ctx);
        var v = await UntilEnded(session);
        Assert.Equal("done", v.Ended);
        Assert.DoesNotContain(ShootMode.Dither, session.ModeHistory);
        Assert.DoesNotContain(ShootMode.Paused, session.ModeHistory);
        Assert.True(v.GuideStopped);
    }

    [Fact]
    public async Task CX05_다시_가운데는_가이딩을_멈춘_뒤에_하고_끝나면_가이딩을_다시_켠다()
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        var dev = new SimulatedShootDevices(sim, faults);
        var session = new ShootSession(dev, new PrepareMode(true), NullLogger<ShootSession>.Instance);
        var results = new PrepResults();
        results.Set(new FocusResult(false, 12340, 2.1, 12.4, DateTimeOffset.Now));
        var ctx = Harness.TargetContext(results, new InMemoryPrepMemory(), Harness.Plan() with { EstimatedFrames = 3 });
        faults.Arm("shoot.wind");
        faults.Arm("shoot.reselectfail"); // 별 다시 고르기 실패 → 다시 가운데
        session.Start(ctx);
        Assert.Equal("done", (await UntilEnded(session)).Ended);
        var order = string.Join(",", dev.Calls);
        Assert.Contains("reselect,stop,recenter,resume", order);
    }

    [Fact]
    public async Task CX09_노출을_멈추지_못해_저장된_사진은_제외로_센다()
    {
        var r = NewShoot(10);
        r.Faults.Arm("shoot.abortfail");
        r.Session.Start(r.Ctx);
        var v = await UntilEnded(r.Session);
        Assert.Equal("mount", v.Ended);
        Assert.Equal(1, v.Excluded);
        Assert.Equal(1, v.Tally["F"]);
    }

    [Fact]
    public async Task CX03_홈이나_추적_끄기를_확인하지_못하면_연결을_끊기_전에_묻는다()
    {
        var (flow, sim, results) = WrapNight();
        sim.Faults.Arm("wrap.tracking");
        await flow.Wrap.Press("skip"); // 보정 프레임 건너뛰기 → 바로 장비 정리
        var v = await flow.Wrap.Until(x => x.Has("confirmed"), "적도의 확인 질문");
        Assert.Equal("적도의를 정리하지 못했습니다", v.Current!.Guide.Title);
        Assert.Null(results.Get<PackResult>()); // 아직 연결 끊기·프로그램 닫기 안 함
        await flow.Wrap.Press("retry");
        await flow.Wrap.Until(x => x.Ready, "요약");
        var pack = results.Get<PackResult>()!;
        Assert.True(pack.TrackingOff && pack.SafeToPowerOff && !pack.UserConfirmed);
    }

    [Fact]
    public async Task CX08_다크를_연속으로_못_찍으면_멈추고_고르게_한다()
    {
        var (flow, sim, results) = WrapNight();
        sim.Faults.Arm("wrap.dark");
        await flow.Wrap.Press("panel");
        await flow.Wrap.Press("covered");
        var v = await flow.Wrap.Until(x => x.Has("keep"), "다크 실패 질문");
        Assert.Contains("다크", v.Current!.Guide.Title);
        await flow.Wrap.Press("retry");
        await flow.Wrap.RunToReady();
        Assert.Equal(20, results.Get<DarkResult>()!.Sets.Single().Count);
    }

    // ── 마무리 ─────────

    private static (PrepareFlow Flow, Harness.Sim Sim, PrepResults Results) WrapNight(int exposure = 180)
    {
        var (flow, sim) = Harness.Flow();
        var results = flow.ResultsFor(new DateOnly(2026, 10, 7));
        results.Set(new NightShootResult([new TargetShots("m31", "M31", 40, 2, exposure, 800, new Dictionary<string, int>(), "D:/Astro/m31")]));
        flow.StartWrap(Harness.RigContext(results, new InMemoryPrepMemory()), new DateOnly(2026, 10, 7));
        return (flow, sim, results);
    }

    [Fact]
    public async Task 마무리는_플랫_다크_장비_정리를_거쳐_요약으로_끝난다()
    {
        var (flow, _, results) = WrapNight();
        var v = await flow.Wrap.RunToReady();
        Assert.True(v.Ready);
        Assert.Equal(["flat", "dark", "pack"], v.Tasks.Select(t => t.Id));
        Assert.Equal(30, results.Get<FlatResult>()!.Count);
        var dark = results.Get<DarkResult>()!;
        Assert.Equal(30, dark.FlatDarks);
        Assert.Equal(new DarkSet(180, 800, 20), dark.Sets.Single()); // 그날 찍은 노출·ISO, 기본 20장
        Assert.True(dark.Homed);
        Assert.True(results.Get<PackResult>()!.Closed);
    }

    [Fact]
    public async Task 다크_장수는_5장씩_고를_수_있고_여기까지만_찍을_수_있다()
    {
        var (flow, _, results) = WrapNight();
        await flow.Wrap.Press("panel");
        await flow.Wrap.Press("less");
        await flow.Wrap.Press("less");
        var asked = await flow.Wrap.Until(x => x.Has("covered") && x.Current!.Center.Readout?.Big == "10", "10장");
        Assert.Equal(10, asked.Current!.Center.Readout!.Values["count"]);
        await flow.Wrap.Press("covered");
        await flow.Wrap.Press("stop-here");
        await flow.Wrap.Until(x => x.Ready, "요약");
        var n = results.Get<DarkResult>()!.Sets.Sum(s => s.Count);
        Assert.InRange(n, 1, 10); // 모의는 기다리지 않아 누르기 전에 다 찍을 수도 있다
    }

    [Fact]
    public async Task 보정_프레임을_건너뛰면_바로_장비_정리()
    {
        var (flow, _, results) = WrapNight();
        await flow.Wrap.Press("skip");
        await flow.Wrap.Until(x => x.Ready, "요약");
        Assert.True(results.Get<FlatResult>()!.Skipped);
        Assert.True(results.Get<DarkResult>()!.Skipped);
        Assert.NotNull(results.Get<PackResult>());
    }

    [Fact]
    public async Task 패널이_너무_밝으면_어둡게_하자고_한다()
    {
        var (flow, sim, _) = WrapNight();
        sim.Faults.Arm("wrap.bright");
        await flow.Wrap.Press("panel");
        var v = await flow.Wrap.Until(x => x.Has("retry"), "너무 밝음");
        Assert.Equal("패널이 너무 밝아요", v.Current!.Guide.Title);
        await flow.Wrap.Press("retry");
        await flow.Wrap.RunToReady();
    }
}
