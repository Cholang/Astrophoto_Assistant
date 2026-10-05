using Astro.Server.Prepare.Flow;

namespace Astro.Server.Tests.Prepare;

/// <summary>실제 작업 7개 + 모의 장비로 준비 전체 흐름</summary>
public class PrepareFlowTests
{
    [Fact]
    public async Task 끝까지_가면_모든_작업이_끝나고_결과_기록이_남는다()
    {
        var (runner, _) = Harness.RealTasks();
        var ctx = Harness.Context();
        runner.Start(ctx);
        var v = await runner.RunToReady();

        Assert.True(v.Ready);
        Assert.Equal(["polar", "calibration", "slew", "focus", "center", "guiding", "test"], v.Tasks.Select(t => t.Id));
        Assert.All(v.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
        Assert.NotNull(ctx.Results.Get<PolarResult>());
        Assert.Equal("A", ctx.Results.Get<CalibrationResult>()!.Position);
        Assert.NotNull(ctx.Results.Get<SlewResult>());
        Assert.Equal(2.1, ctx.Results.Get<FocusResult>()!.Hfr);
        Assert.True(ctx.Results.Get<CenterResult>()!.ErrorArcmin <= 1);
        Assert.Equal("충분해요", ctx.Results.Get<GuidingResult>()!.Grade);
        Assert.NotNull(ctx.Results.Get<TestShotResult>());
    }

    [Fact]
    public async Task 가이더가_없으면_캘리브레이션과_가이딩은_건너뛴다()
    {
        var (runner, _) = Harness.RealTasks();
        runner.Start(Harness.Context(hasGuider: false));
        var v = await runner.RunToReady();
        Assert.Equal(PrepTaskStatus.Skipped, v.Row("calibration"));
        Assert.Equal(PrepTaskStatus.Skipped, v.Row("guiding"));
        Assert.Equal(PrepTaskStatus.Done, v.Row("test"));
    }

    [Fact]
    public async Task 캘리브레이션_위치A에서_별이_없으면_B로_간다()
    {
        var (runner, sim) = Harness.RealTasks();
        var ctx = Harness.Context();
        sim.Faults.Arm("calibration.star");
        runner.Start(ctx);
        await runner.RunToReady();
        Assert.Equal("B", ctx.Results.Get<CalibrationResult>()!.Position);
    }

    [Fact]
    public async Task 센터링이_안되면_초점부터_다시_하고_센터링으로_돌아온다()
    {
        var (runner, sim) = Harness.RealTasks();
        var ctx = Harness.Context();
        sim.Faults.Arm("center.solve");
        runner.Start(ctx);
        // 센터링이 "초점 다시 맞추기"를 물을 때까지 주 버튼을 누른다
        for (var i = 0; i < 40 && !runner.View().Has("refocus"); i++)
        {
            var v = await runner.Until(x => x.Has("refocus") || x.Current?.Center.Actions.Count > 0, "센터링 실패");
            if (v.Has("refocus")) break;
            runner.Act(v.Current!.Center.Actions.First(a => a.Primary).Id);
            await Task.Delay(5);
        }
        await runner.Press("refocus");
        var after = await runner.Until(x => x.Current?.TaskId == "focus", "초점 다시");
        Assert.Equal(PrepTaskStatus.NeedsRecheck, after.Row("center"));
        Assert.Equal(PrepTaskStatus.Done, after.Row("slew")); // 앞 작업은 그대로
        var end = await runner.RunToReady();
        Assert.All(end.Tasks, t => Assert.Equal(PrepTaskStatus.Done, t.Status));
    }

    [Fact]
    public async Task 대상이_낮으면_이동을_막고_기다리면_이동할_수_있다()
    {
        var (runner, sim) = Harness.RealTasks();
        sim.Faults.Arm("slew.low");
        runner.Start(Harness.Context());
        for (var i = 0; i < 20 && !runner.View().Has("wait"); i++)
        {
            var v = await runner.Until(x => x.Current?.Center.Actions.Count > 0, "버튼");
            if (v.Has("wait")) break;
            runner.Act(v.Current!.Center.Actions.First(a => a.Primary).Id);
            await Task.Delay(5);
        }
        await runner.Press("move"); // 아직 낮은데 이동 → 막힘
        var blocked = await runner.Until(v => v.Current?.Center.Status?.Tone == Tone.Fail, "이동 막힘");
        Assert.Contains("떠오르지 않아", blocked.Current!.Center.Status!.Text);
        await runner.Press("wait");
        await runner.Press("move"); // 올라온 뒤 이동
        var done = await runner.Until(v => v.Row("slew") == PrepTaskStatus.Done, "이동 끝");
        Assert.Equal(PrepTaskStatus.Done, done.Row("slew"));
    }
}
