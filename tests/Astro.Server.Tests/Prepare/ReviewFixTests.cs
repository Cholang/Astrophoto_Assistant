using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>Codex 코드 리뷰(REVIEW_CODEX.md 12절 CX-PREP-CODE-01~06)의 후속 검증 기준</summary>
public class ReviewFixTests
{
    private static readonly string[] Ids = ["polar", "calibration", "slew", "focus", "center", "guiding", "test"];

    private static (PrepareRunner Runner, Dictionary<string, FakeTask> Tasks, List<string> Log) Make()
    {
        var log = new List<string>();
        var tasks = Ids.ToDictionary(id => id, id => new FakeTask(id, log));
        tasks["guiding"].KeepsRunning = true;
        return (new PrepareRunner(Ids.Select(id => (IPrepTask)tasks[id]), NullLogger<PrepareRunner>.Instance), tasks, log);
    }

    /// <summary>시험 사진까지 끝내고 "촬영 시작"이 보일 때까지 (누르지는 않음)</summary>
    private static async Task ToTestDone(PrepareRunner runner)
    {
        for (var i = 0; i < 20; i++)
        {
            var v = await runner.Until(x => x.Has("next") || x.Has("start"), "끝 버튼");
            if (v.Has("start")) return;
            var from = v.Current!.TaskId;
            Assert.Null(runner.Act("next"));
            await runner.Until(x => x.Current?.TaskId != from, "다음 작업");
        }
    }

    private static int IndexOf(List<string> log, Func<string, bool> match, int from = 0)
    {
        lock (log) return log.FindIndex(from, l => match(l));
    }

    [Fact]
    public async Task CODE01_재센터링_전에_계속_도는_가이딩을_멈추고_확인한다()
    {
        var (runner, _, log) = Make();
        runner.Start(Harness.Context());
        await ToTestDone(runner);
        var before = IndexOf(log, l => l.StartsWith("run:center:"));

        await runner.RedoAsync("center", fromRunning: true);
        await runner.Until(x => x.Current?.TaskId == "center", "센터링 다시");
        // 작업 실행은 비동기 — 두 번째 실행 기록이 남을 때까지
        for (var i = 0; i < 200 && IndexOf(log, l => l.StartsWith("run:center:"), before + 1) < 0; i++) await Task.Delay(10);

        var stop = IndexOf(log, l => l == "stop:guiding:True");
        var rerun = IndexOf(log, l => l.StartsWith("run:center:"), before + 1);
        Assert.True(stop >= 0, "가이딩 정지를 불러야 한다");
        Assert.True(rerun > stop, "가이딩 정지 확인이 센터링 재실행보다 먼저");
        var v = runner.View();
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("guiding"));
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("test"));
    }

    [Fact]
    public async Task CODE01_가이딩_정지를_확인하지_못하면_재센터링하지_않는다()
    {
        var (runner, tasks, log) = Make();
        runner.Start(Harness.Context());
        await ToTestDone(runner);
        tasks["guiding"].StopConfirms = false;
        int Runs() { lock (log) return log.Count(l => l.StartsWith("run:center:")); }
        var runsBefore = Runs();

        await runner.RedoAsync("center", fromRunning: true);
        await runner.Until(x => x.Has("device-check"), "장비 상태 다시 확인");
        Assert.Equal(runsBefore, Runs());

        tasks["guiding"].StopConfirms = true;
        Assert.Null(runner.Act("device-check"));
        await runner.Until(x => x.Current?.TaskId == "center", "확인 뒤 센터링");
    }

    [Fact]
    public async Task CODE03_작업이_끝나_다음을_기다릴_때_중단하면_다음과_촬영_시작이_사라진다()
    {
        var (runner, _, log) = Make();
        runner.Start(Harness.Context());
        await ToTestDone(runner);

        Assert.True(await runner.AbortAsync());
        var v = runner.View();
        Assert.False(v.Has("start"));
        Assert.False(v.Has("next"));
        Assert.True(v.Has("restart"));
        Assert.False(v.Ready);
        lock (log) Assert.Contains("stop:guiding:True", log); // 계속 돌던 가이딩도 멈춤
        Assert.Equal(PrepTaskStatus.NeedsRecheck, v.Row("guiding"));

        // 다시 시작: 멈춘 가이딩부터
        Assert.Null(runner.Act("restart"));
        await runner.Until(x => x.Current?.TaskId == "guiding", "가이딩부터 다시");
    }

    // ── 작업 단독 (가짜 화면 통로) ─────────

    private sealed class ScriptRun(PrepContext ctx, Func<IReadOnlyList<PrepAction>, string> pick) : ITaskRun
    {
        public PrepContext Context => ctx;
        public int RunId => 1;
        public List<string> Statuses { get; } = [];
        public List<string> Asked { get; } = [];
        public void SubStep(int index) { }
        public void Guide(string title, string text) { }
        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) { }
        public void ClearReadout() { }
        public void Status(string? text, Tone tone = Tone.Busy) { if (text is not null) lock (Statuses) Statuses.Add(text); }
        public void Live(string kind, string? url = null, object? data = null) { }
        public Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            lock (Asked) Asked.Add(string.Join(",", actions.Select(a => a.Id)));
            var id = pick(actions);
            // ""이면 누르지 않는다 (작업이 질문을 취소할 때까지)
            if (id == "") return Task.Delay(Timeout.Infinite, ct).ContinueWith<string>(_ => throw new OperationCanceledException(ct), TaskScheduler.Default);
            return Task.FromResult(id);
        }
    }

    private static string Primary(IReadOnlyList<PrepAction> actions) => actions.First(a => a.Primary).Id;

    /// <summary>id가 있으면 그것을, 없으면 주 버튼을 누른다</summary>
    private static Func<IReadOnlyList<PrepAction>, string> Prefer(string id) => actions =>
        actions.Any(a => a.Id == id) ? id
        : actions is [{ Id: "stop" or "end-wait" }] ? "" // 진행 중 "멈춤"·"기다리기 끝"은 누르지 않는다
        : Primary(actions);

    // ── 작업 실패 시 건너뛰기 (2026-10-08 사용자 결정, 제미나이 의견 반영) ─────────

    [Fact]
    public async Task 건너뛰기_극축_정렬은_별을_못_찾으면_건너뛰고_카메라를_돌려준다()
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        faults.Arm("polar.stars");
        var task = new PolarTask(new SimulatedPolarDevices(sim, faults));
        var ctx = Harness.Context();
        var run = new ScriptRun(ctx, Prefer("skip"));
        var done = Assert.IsType<Completed>(await task.RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout));
        Assert.True(Assert.IsType<PolarResult>(done.Result).Skipped);
        ctx.Results.Set(done.Result);
        Assert.True((await task.CheckEndStateAsync(ctx, CancellationToken.None)).Ok); // SharpCap 닫힘·카메라 PHD2
    }

    [Fact]
    public async Task 건너뛰기_가이딩을_못하면_가이딩_없이_진행하고_노출을_줄일지_묻는다()
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        faults.Arm("guiding.star");
        var task = new GuidingTask(new SimulatedGuidingDevices(sim, faults));
        var ctx = Harness.Context(plan: Harness.Plan()); // 120초 계획
        var run = new ScriptRun(ctx, Prefer("noguide"));
        var done = Assert.IsType<Completed>(await task.RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout));
        Assert.True(Assert.IsType<GuidingResult>(done.Result).Skipped);
        Assert.Contains("shorter,keep", run.Asked);
        Assert.Equal(GuidingTask.UnguidedExposure, ctx.ExposureSeconds); // 주 버튼 = 줄이기
        Assert.False(ctx.HasGuider); // 그날 밤 가이딩 없음
        ctx.Results.Set(done.Result);
        Assert.True((await task.CheckEndStateAsync(ctx, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task 건너뛰기_이동이_안_되면_지금_위치를_목표로_하고_센터링도_건너뛴다()
    {
        var sim = new SimOptions { Speed = 0 };
        var faults = new SimFaults();
        faults.Arm("slew.move");
        var ctx = Harness.Context(plan: Harness.Plan());
        ctx.IgnoreAltitude = true;
        var slew = new SlewTask(new SimulatedSlewDevices(sim, faults));
        var done = Assert.IsType<Completed>(await slew.RunAsync(new ScriptRun(ctx, Prefer("here")), CancellationToken.None).WaitAsync(Harness.Timeout));
        Assert.True(Assert.IsType<SlewResult>(done.Result).AcceptedHere);
        ctx.Results.Set(done.Result);
        var center = await new CenterTask(new SimulatedCenterDevices(sim, faults)).RunAsync(new ScriptRun(ctx, Primary), CancellationToken.None).WaitAsync(Harness.Timeout);
        Assert.True(Assert.IsType<CenterResult>(Assert.IsType<Completed>(center).Result).Skipped);
    }

    [Fact]
    public async Task 건너뛰기_시험_사진을_못_찍으면_건너뛸_수_있다()
    {
        var faults = new SimFaults();
        faults.Arm("test.expose");
        var task = new TestShotTask(new SimulatedTestShotDevices(new SimOptions { Speed = 0 }, faults));
        var ctx = Harness.Context();
        var done = Assert.IsType<Completed>(await task.RunAsync(new ScriptRun(ctx, Prefer("skip")), CancellationToken.None).WaitAsync(Harness.Timeout));
        Assert.True(Assert.IsType<TestShotResult>(done.Result).Skipped);
    }

    [Fact]
    public async Task CODE02_이동_중_정지를_확인하지_못하면_다시_이동을_보이지_않는다()
    {
        var faults = new SimFaults();
        var task = new SlewTask(new SimulatedSlewDevices(new SimOptions { Speed = 0.05 }, faults));
        var ctx = Harness.Context();
        ctx.IgnoreAltitude = true;
        faults.Arm("stop.fail"); // 첫 정지는 확인 실패
        var stopped = false;
        var run = new ScriptRun(ctx, actions =>
        {
            if (actions[0].Id == "stop") { if (stopped) return ""; stopped = true; return "stop"; }
            return Primary(actions);
        });
        Assert.IsType<Completed>(await task.RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout));
        // 멈춤 → (확인 실패) 장비 상태 다시 확인 → (확인됨) 다시 이동. 확인 전에 다시 이동이 나오면 안 된다
        var seq = run.Asked.Where(a => a != "stop").ToList();
        Assert.Equal("recheck-stop", seq[0]);
        Assert.Equal("move", seq[1]);
        Assert.Contains(run.Statuses, s => s.Contains("확인하지 못했습니다"));
    }

    /// <summary>조절량이 한 번도 오지 않는 극축 정렬 장비 (나머지는 모의 그대로)</summary>
    private sealed class SilentPolar(IPolarDevices inner) : IPolarDevices
    {
        public Task<DeviceResult> SetSiderealTrackingAsync(CancellationToken ct) => inner.SetSiderealTrackingAsync(ct);
        public Task<DeviceResult> HandOverGuideCameraAsync(CancellationToken ct) => inner.HandOverGuideCameraAsync(ct);
        public Task<DeviceResult> StartSharpCapAsync(CancellationToken ct) => inner.StartSharpCapAsync(ct);
        public Task<PoleFindResult> FindPoleAsync(Action<PoleProgress> progress, CancellationToken ct) => inner.FindPoleAsync(progress, ct);
        public async IAsyncEnumerable<PolarOffset> WatchOffsetsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<DeviceResult> GiveBackGuideCameraAsync(CancellationToken ct) => inner.GiveBackGuideCameraAsync(ct);
        public Task<double?> GuidePixelScaleAsync(CancellationToken ct) => inner.GuidePixelScaleAsync(ct);
        public bool OffsetUnitVerified => inner.OffsetUnitVerified;
        public Task<bool> StopAsync(CancellationToken ct) => inner.StopAsync(ct);
        public Task<PolarEndState> ReadEndStateAsync(CancellationToken ct) => inner.ReadEndStateAsync(ct);
    }

    [Fact]
    public async Task CODE04_조절량을_못_받으면_정렬_완료를_눌러도_끝내지_않는다()
    {
        var task = new PolarTask(new SilentPolar(new SimulatedPolarDevices(new SimOptions { Speed = 0 }, new SimFaults())));
        var ctx = Harness.Context();
        using var cts = new CancellationTokenSource();
        var run = new ScriptRun(ctx, Primary);
        var running = task.RunAsync(run, cts.Token);
        await Task.Delay(2500);
        Assert.False(running.IsCompleted, "측정 없이 끝나면 안 된다");
        lock (run.Asked) Assert.True(run.Asked.Count(a => a == "align-done") >= 2);
        lock (run.Statuses) Assert.Contains(run.Statuses, s => s.Contains("받지 못했습니다"));
        Assert.Null(ctx.Memory.LastPolarAlignedAt);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task CODE05_노출이_실패하면_사진을_받지_않고_다시_찍기를_묻는다()
    {
        var faults = new SimFaults();
        faults.Arm("test.expose");
        var task = new TestShotTask(new SimulatedTestShotDevices(new SimOptions { Speed = 0 }, faults));
        var run = new ScriptRun(Harness.Context(), Primary);
        Assert.IsType<Completed>(await task.RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout)); // 다시 찍기 뒤 성공
        Assert.Equal("retry,skip", run.Asked[0]); // 2026-10-08: 시험 사진 건너뛰기도 함께
        var failed = run.Statuses.FindIndex(s => s.Contains("노출하지 못했습니다"));
        Assert.True(failed >= 0);
        Assert.DoesNotContain(run.Statuses.Take(failed), s => s.Contains("내려받는"));
    }

    [Fact]
    public async Task CODE06_극축_정렬을_다시_하면_이전_캘리브레이션을_재사용하지_않는다()
    {
        var devices = new SimulatedCalibrationDevices(new SimOptions { Speed = 0 }, new SimFaults());
        var memory = new InMemoryPrepMemory();
        var first = new ScriptRun(Harness.Context(memory: memory), Primary);
        Assert.IsType<Completed>(await new CalibrationTask(devices).RunAsync(first, CancellationToken.None).WaitAsync(Harness.Timeout));

        memory.LastPolarAlignedAt = DateTimeOffset.Now.AddSeconds(1); // 그 뒤에 극축 정렬을 다시 함
        var second = new ScriptRun(Harness.Context(memory: memory), Primary);
        var done = Assert.IsType<Completed>(await new CalibrationTask(devices).RunAsync(second, CancellationToken.None).WaitAsync(Harness.Timeout));
        Assert.DoesNotContain("reuse,new", second.Asked);
        Assert.False(Assert.IsType<CalibrationResult>(done.Result).Reused);
    }
}
