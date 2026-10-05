using Astro.Server.Prepare.Flow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>러너만 (가짜 작업): 순서·끝 상태 약속·다시 하기 의존표·멈춤 확인·늦은 갱신·오래된 값 (CX-PREP-IMPL-01~05)</summary>
public class RunnerTests
{
    private static readonly string[] Ids = ["polar", "calibration", "slew", "focus", "center", "guiding", "test"];

    private static (PrepareRunner Runner, Dictionary<string, FakeTask> Tasks, List<string> Log) Make()
    {
        var log = new List<string>();
        var tasks = Ids.ToDictionary(id => id, id => new FakeTask(id, log));
        return (new PrepareRunner(Ids.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance), tasks, log);
    }

    [Fact]
    public async Task 작업은_등록_순서대로_하고_끝_버튼은_다음_작업_이름이다()
    {
        var (runner, _, log) = Make();
        runner.Start(Harness.Context());
        var v = await runner.Until(x => x.Has("next"), "첫 작업 끝");
        Assert.Equal("calibration 시작", v.Current!.Center.Actions.First(a => a.Primary).Label);
        await runner.RunToReady();
        Assert.Equal(Ids.Select(id => $"run:{id}"), log.Where(l => l.StartsWith("run:")).Select(l => l[..l.LastIndexOf(':')]));
    }

    [Fact]
    public async Task 끝_상태_약속이_어긋나면_다음으로_넘어가지_않는다()
    {
        var (runner, tasks, _) = Make();
        tasks["polar"].EndStateOk = false;
        runner.Start(Harness.Context());
        var v = await runner.Until(x => x.Has("endcheck"), "약속 어김");
        Assert.Equal(PrepTaskStatus.Failed, v.Row("polar"));
        Assert.False(v.Has("next"));
        Assert.Contains("약속 어김", v.Current!.Center.Status!.Text);

        tasks["polar"].EndStateOk = true;
        await runner.Press("endcheck");
        var ok = await runner.Until(x => x.Has("next"), "다시 확인 후 통과");
        Assert.Equal(PrepTaskStatus.Done, ok.Row("polar"));
    }

    [Fact]
    public async Task 다시_하기는_영향받는_뒤_작업만_다시_확인_필요로_만든다()
    {
        var (runner, _, _) = Make();
        runner.Start(Harness.Context());
        await runner.RunToReady();
        await runner.RedoAsync("slew", fromRunning: true);
        var v = await runner.Until(x => x.Current?.TaskId == "slew", "이동 다시");
        Assert.Equal(PrepTaskStatus.Done, v.Row("polar"));
        Assert.Equal(PrepTaskStatus.Done, v.Row("calibration"));
        Assert.Equal(PrepTaskStatus.Done, v.Row("focus"));       // 이동을 다시 해도 초점은 그대로
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("center"));
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("guiding"));
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("test"));
        Assert.False(v.Ready);
    }

    [Fact]
    public async Task 도중에_멈추면_정지를_확인한_뒤에야_다음_작업을_시작한다()
    {
        var (runner, tasks, log) = Make();
        tasks["polar"].Hang = true;
        runner.Start(Harness.Context());
        await runner.Until(x => x.Current?.TaskId == "polar" && x.Current.Readout() is not null, "① 진행 중");

        await runner.RedoAsync("polar", fromRunning: true); // 진행 중 작업을 멈추고 다시
        tasks["polar"].Hang = false;
        // 다시 시작은 비동기로 돈다 → 두 번째 실행 기록이 남을 때까지 기다린 뒤 순서를 본다
        for (var i = 0; i < 200; i++)
        {
            lock (log) if (log.Count(l => l.StartsWith("run:polar:")) >= 2) break;
            await Task.Delay(10);
        }
        lock (log)
        {
            var stop = log.IndexOf("stop:polar:True");
            var rerun = log.FindLastIndex(l => l.StartsWith("run:polar:"));
            Assert.True(stop >= 0, "정지 확인을 불러야 한다");
            Assert.True(stop < rerun, "정지 확인이 다시 시작보다 먼저");
        }
    }

    [Fact]
    public async Task 정지를_확인하지_못하면_장비_상태_확인_필요에서_멈춘다()
    {
        var (runner, tasks, log) = Make();
        tasks["polar"].Hang = true;
        tasks["polar"].StopConfirms = false;
        runner.Start(Harness.Context());
        await runner.Until(x => x.Current?.TaskId == "polar" && x.Current.Readout() is not null, "① 진행 중");

        Assert.False(await runner.AbortAsync());
        var v = runner.View();
        Assert.True(v.Has("device-check"));
        Assert.Contains("확인하지 못했습니다", v.Current!.Center.Status!.Text);
        Assert.Equal(Tone.Fail, v.Current.Center.Status.Tone);

        tasks["polar"].StopConfirms = true;
        await runner.Press("device-check");
        await runner.Until(x => !x.Has("device-check"), "다시 확인 후 풀림");
        lock (log) Assert.Contains("stop:polar:True", log);
    }

    [Fact]
    public async Task 지난_실행의_늦은_갱신은_버린다()
    {
        var (runner, tasks, _) = Make();
        tasks["polar"].Hang = true;
        tasks["polar"].IgnoreCancel = true;
        runner.Start(Harness.Context());
        await runner.Until(x => x.Current?.TaskId == "polar" && x.Current.Readout() is not null, "① 진행 중");
        var oldRun = tasks["polar"].LastRun!;

        tasks["polar"].Hang = false;
        await runner.RedoAsync("polar", fromRunning: true);
        var v = await runner.Until(x => x.Current?.RunId != oldRun.RunId, "새 실행");

        oldRun.Readout("x", "늦은 값", "지난 실행", Tone.Fail);
        oldRun.Guide("늦은 제목", "지난 실행");
        var after = runner.View();
        Assert.NotEqual("늦은 값", after.Current!.Center.Readout?.Big);
        Assert.NotEqual("늦은 제목", after.Current.Guide.Title);
    }

    [Fact]
    public async Task 갱신이_끊긴_값은_오래된_값으로_표시한다()
    {
        var (runner, tasks, _) = Make();
        tasks["polar"].Hang = true;
        runner.Start(Harness.Context());
        var v = await runner.Until(x => x.Current?.TaskId == "polar" && x.Current.Readout() is not null, "① 진행 중");
        Assert.Equal(Freshness.Fresh, v.Current!.Center.Readout!.Freshness);

        runner.CheckStaleNow(DateTimeOffset.Now.AddSeconds(5));
        Assert.Equal(Freshness.Stale, runner.View().Current!.Center.Readout!.Freshness);
        await runner.AbortAsync();
    }

    [Fact]
    public async Task 끝난_뒤_촬영_시작을_누르면_준비_끝()
    {
        var (runner, _, _) = Make();
        runner.Start(Harness.Context());
        var v = await runner.RunToReady();
        Assert.True(v.Ready);
        Assert.Null(v.Current!.Center.Actions.FirstOrDefault(a => a.Id == "next"));
    }
}

internal static class ViewExt
{
    public static ReadoutView? Readout(this CurrentView c) => c.Center.Readout;
}
