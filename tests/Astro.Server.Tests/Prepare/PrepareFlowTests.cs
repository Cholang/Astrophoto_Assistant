using Astro.Server.Prepare.Flow;

namespace Astro.Server.Tests.Prepare;

/// <summary>실제 작업 8개 + 모의 장비로 두 묶음 흐름: 장비 준비(극축 정렬 · 캘리브레이션 · 초점) → 대상(이동 · 센터링 · 초점 확인 · 가이딩 · 시험 사진)</summary>
public class PrepareFlowTests
{
    private static readonly DateOnly Evening = new(2026, 10, 7);

    private sealed record Night(PrepareFlow Flow, Harness.Sim Sim, PrepResults Results, InMemoryPrepMemory Memory)
    {
        public PrepContext Target(Astro.Server.Assistant.PreparationPlan? plan = null, bool hasGuider = true) =>
            Harness.TargetContext(Results, Memory, plan, hasGuider);
    }

    private static Night NewNight()
    {
        var (flow, sim) = Harness.Flow();
        return new Night(flow, sim, flow.ResultsFor(Evening), new InMemoryPrepMemory());
    }

    /// <summary>장비 준비를 끝까지 ("대상 고르기"까지 누름)</summary>
    private static async Task<Night> RigDone(bool hasGuider = true, Action<Night>? arm = null)
    {
        var n = NewNight();
        arm?.Invoke(n);
        n.Flow.StartRig(Harness.RigContext(n.Results, n.Memory, hasGuider), Evening);
        var v = await n.Flow.Rig.RunToReady();
        Assert.True(n.Flow.Rig.AllDone, Harness.Describe(v));
        return n;
    }

    [Fact]
    public async Task 장비_준비를_끝내고_대상을_끝까지_가면_결과_기록이_남는다()
    {
        var n = await RigDone();
        var rig = n.Flow.Rig.View();
        Assert.Equal(["polar", "calibration", "focus"], rig.Tasks.Select(t => t.Id));
        Assert.Equal("rig", rig.Group);

        Assert.Null(n.Flow.StartTarget(n.Target()));
        var v = await n.Flow.Target.RunToReady();
        Assert.True(v.Ready);
        Assert.Equal("target", v.Group);
        Assert.Equal(["slew", "center", "focuscheck", "guiding", "test"], v.Tasks.Select(t => t.Id));
        Assert.All(v.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
        Assert.NotNull(n.Results.Get<PolarResult>());
        Assert.Equal("A", n.Results.Get<CalibrationResult>()!.Position);
        Assert.Equal(2.1, n.Results.Get<FocusResult>()!.Hfr);
        Assert.NotNull(n.Results.Get<SlewResult>());
        Assert.Equal(2.3, n.Results.Get<CenterResult>()!.Hfr);
        Assert.False(n.Results.Get<FocusCheckResult>()!.Refocused); // 기온·별 크기 모두 괜찮아 그대로
        Assert.Equal("충분해요", n.Results.Get<GuidingResult>()!.Grade);
        Assert.NotNull(n.Results.Get<TestShotResult>());
    }

    [Fact]
    public async Task 장비_준비의_끝_버튼은_대상_고르기()
    {
        var n = NewNight();
        n.Flow.StartRig(Harness.RigContext(n.Results, n.Memory), Evening);
        await PressUntil(n.Flow.Rig, "flow:target");
        var v = n.Flow.Rig.View();
        Assert.Equal("대상 고르기", v.Current!.Center.Actions.First(a => a.Primary).Label);
    }

    [Fact]
    public async Task 장비_준비가_끝나지_않으면_대상을_시작하지_않는다()
    {
        var n = NewNight();
        Assert.NotNull(n.Flow.StartTarget(n.Target()));
        Assert.False(n.Flow.Target.View().Started);
    }

    [Fact]
    public async Task 가이더가_없으면_캘리브레이션과_가이딩은_건너뛴다()
    {
        var n = await RigDone(hasGuider: false);
        Assert.Equal(PrepTaskStatus.Skipped, n.Flow.Rig.View().Row("calibration"));
        Assert.Null(n.Flow.StartTarget(n.Target(hasGuider: false)));
        var v = await n.Flow.Target.RunToReady();
        Assert.Equal(PrepTaskStatus.Skipped, v.Row("guiding"));
        Assert.Equal(PrepTaskStatus.Done, v.Row("test"));
    }

    [Fact]
    public async Task 캘리브레이션_위치A에서_별이_없으면_B로_간다()
    {
        var n = await RigDone(arm: x => x.Sim.Faults.Arm("calibration.star"));
        Assert.Equal("B", n.Results.Get<CalibrationResult>()!.Position);
    }

    [Fact]
    public async Task 대상을_바꿔도_장비_준비_결과는_그대로이고_대상만_처음부터_한다()
    {
        var n = await RigDone();
        var polar = n.Results.Get<PolarResult>();
        Assert.Null(n.Flow.StartTarget(n.Target()));
        await n.Flow.Target.RunToReady();

        // 다른 대상 (새로 확정한 계획)
        Assert.Null(n.Flow.StartTarget(n.Target(Harness.Plan(DateTimeOffset.Now.AddMinutes(1)))));
        var fresh = await n.Flow.Target.Until(v => !v.Ready && v.Row("test") == PrepTaskStatus.Pending, "대상 새로 시작");
        Assert.Equal(PrepTaskStatus.Pending, fresh.Row("guiding"));
        Assert.True(n.Flow.Rig.AllDone);
        Assert.Same(polar, n.Results.Get<PolarResult>()); // 장비 준비 결과는 그대로
        var end = await n.Flow.Target.RunToReady();
        Assert.All(end.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
    }

    [Fact]
    public async Task 센터링이_끝나면_이_대상으로_확정과_다른_대상을_묻는다()
    {
        var n = await RigDone();
        n.Flow.StartTarget(n.Target());
        await n.Flow.Target.Press("next"); // 이동 끝 → 센터링 시작
        var v = await n.Flow.Target.Until(x => x.Has("flow:replan"), "센터링 끝");
        Assert.Equal("이 대상으로 확정", v.Current!.Center.Actions.First(a => a.Primary).Label);
        Assert.Null(n.Flow.Target.Act("flow:replan")); // 화면이 계획으로 간다 (러너는 그대로)
    }

    [Fact]
    public async Task 기온이_크게_바뀌면_초점을_다시_맞출지_묻고_다시_맞춘다()
    {
        var n = await RigDone();
        n.Sim.Faults.Arm("focus.temp");
        n.Flow.StartTarget(n.Target());
        await PressUntil(n.Flow.Target, "refocus");
        var asked = n.Flow.Target.View();
        Assert.Contains("기온", asked.Current!.Guide.Text);
        await n.Flow.Target.Press("refocus");
        await n.Flow.Target.RunToReady();
        Assert.True(n.Results.Get<FocusCheckResult>()!.Refocused);
    }

    [Fact]
    public async Task 센터링_사진의_별이_커지면_초점을_다시_맞출지_묻는다()
    {
        var n = await RigDone();
        n.Sim.Faults.Arm("center.hfr");
        n.Flow.StartTarget(n.Target());
        await PressUntil(n.Flow.Target, "refocus");
        Assert.Contains("센터링 사진", n.Flow.Target.View().Current!.Guide.Text);
        await n.Flow.Target.Press("keep");
        var v = await n.Flow.Target.RunToReady();
        Assert.False(n.Results.Get<FocusCheckResult>()!.Refocused);
        Assert.Equal(PrepTaskStatus.Done, v.Row("test"));
    }

    [Fact]
    public async Task 센터링이_안되면_초점_확인에서_다시_맞추고_센터링으로_돌아온다()
    {
        var n = await RigDone();
        n.Sim.Faults.Arm("center.solve");
        n.Flow.StartTarget(n.Target());
        await PressUntil(n.Flow.Target, "refocus");
        await n.Flow.Target.Press("refocus");
        var after = await n.Flow.Target.Until(x => x.Current?.TaskId == "focuscheck", "초점 확인에서 다시 맞춤");
        Assert.Equal(PrepTaskStatus.NeedsRecheck, after.Row("center"));
        Assert.Equal(PrepTaskStatus.Done, after.Row("slew"));
        var end = await n.Flow.Target.RunToReady();
        Assert.All(end.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
        Assert.True(n.Results.Get<FocusCheckResult>()!.Refocused);
        Assert.Equal(PrepTaskStatus.Done, n.Flow.Rig.View().Row("focus")); // 장비 준비의 초점은 그대로
    }

    [Fact]
    public async Task 가이딩이_캘리브레이션을_다시_하자고_하면_대상을_멈추고_장비_준비에서_다시_한_뒤_이어서_한다()
    {
        var n = await RigDone();
        n.Sim.Faults.Arm("guiding.calibration");
        var ctx = n.Target();
        n.Flow.StartTarget(ctx);
        await PressUntil(n.Flow.Target, "recal");
        await n.Flow.Target.Press("recal");

        var handed = await n.Flow.Target.Until(v => v.Handoff == "rig", "장비 준비로 넘김");
        Assert.Equal(PrepTaskStatus.NeedsRecheck, handed.Row("slew")); // 적도의가 캘리브레이션 위치로 가므로 대상 전체를 다시
        await n.Flow.Rig.Until(v => v.Current?.TaskId == "calibration", "캘리브레이션 다시");
        Assert.False(n.Flow.Rig.AllDone);
        await n.Flow.Rig.RunToReady();

        // 같은 계획으로 대상 시작 → 다시 확인할 첫 작업(이동)부터 이어서
        Assert.Null(n.Flow.StartTarget(ctx));
        var resumed = await n.Flow.Target.Until(v => v.Handoff is null && v.Current?.TaskId == "slew", "이어서");
        Assert.Equal(PrepTaskStatus.Running, resumed.Row("slew"));
        var end = await n.Flow.Target.RunToReady();
        Assert.All(end.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
    }

    [Fact]
    public async Task 극축_정렬을_다시_하면_대상_묶음_전체가_다시_확인_필요()
    {
        var n = await RigDone();
        n.Flow.StartTarget(n.Target());
        await n.Flow.Target.RunToReady();

        await n.Flow.Rig.RedoAsync("polar", fromRunning: true);
        var t = await n.Flow.Target.Until(v => v.Handoff == "rig", "대상 멈춤");
        Assert.All(t.Tasks, r => Assert.Equal(PrepTaskStatus.NeedsRecheck, r.Status));
        Assert.False(t.Ready);
        Assert.Null(n.Results.Get<GuidingResult>());
        Assert.Null(n.Results.Get<FocusResult>()); // 장비 준비 안에서도 극축 뒤 작업(캘리브레이션·초점)은 다시
    }

    [Fact]
    public async Task 대상이_낮으면_이동을_막고_기다리면_이동할_수_있다()
    {
        var n = await RigDone();
        n.Sim.Faults.Arm("slew.low");
        n.Flow.StartTarget(n.Target());
        var runner = n.Flow.Target;
        await runner.Until(v => v.Has("wait"), "낮음");
        await runner.Press("move"); // 아직 낮은데 이동 → 막힘
        var blocked = await runner.Until(v => v.Current?.Center.Status?.Tone == Tone.Fail, "이동 막힘");
        Assert.Contains("떠오르지 않아", blocked.Current!.Center.Status!.Text);
        await runner.Press("wait");
        await runner.Press("move"); // 올라온 뒤 이동
        var done = await runner.Until(v => v.Row("slew") == PrepTaskStatus.Done, "이동 끝");
        Assert.Equal(PrepTaskStatus.Done, done.Row("slew"));
    }

    /// <summary>그 버튼이 보일 때까지 주 버튼을 누른다</summary>
    private static async Task PressUntil(PrepareRunner runner, string action)
    {
        for (var i = 0; i < 40; i++)
        {
            var v = await runner.Until(x => x.Has(action) || x.Current?.Center.Actions.Count > 0, action);
            if (v.Has(action)) return;
            runner.Act(v.Current!.Center.Actions.First(a => a.Primary).Id);
            await Task.Delay(5);
        }
        throw new InvalidOperationException($"{action} 버튼이 나오지 않았습니다: " + Harness.Describe(runner.View()));
    }
}
