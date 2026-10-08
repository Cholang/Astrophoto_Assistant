using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Astro.Server.Prepare.Tasks.Calibration;

namespace Astro.Server.Tests.Prepare;

/// <summary>작업 단독 시험: 러너 없이 작업 하나 + 자기 모의 장비 + 가짜 화면 통로. 끝 상태 약속까지 확인</summary>
public class TaskIsolationTests
{
    /// <summary>가짜 화면 통로: 주 버튼(정렬 완료 등)을 바로 누른다. 화면 갱신은 기록만</summary>
    private sealed class AutoRun(PrepContext ctx, Func<IReadOnlyList<PrepAction>, string>? pick = null) : ITaskRun
    {
        public PrepContext Context => ctx;
        public int RunId => 1;
        public List<string> Statuses { get; } = [];
        public List<string> Asked { get; } = [];
        public void SubStep(int index) { }
        public void Guide(string title, string text) { }
        public void Readout(string kind, string big, string caption, Tone tone, IReadOnlyDictionary<string, double>? values = null, bool live = false, bool unverified = false, DateTimeOffset? observedAt = null) { }
        public void ClearReadout() { }
        public void Status(string? text, Tone tone = Tone.Busy) { if (text is not null) Statuses.Add(text); }
        public void Live(string kind, string? url = null, object? data = null) { }
        public Task<string> AskAsync(IReadOnlyList<PrepAction> actions, CancellationToken ct)
        {
            Asked.Add(string.Join(",", actions.Select(a => a.Id)));
            // "멈춤"은 누르지 않는다 (이동이 끝나면 작업이 이 질문을 취소한다)
            if (actions.Count == 1 && actions[0].Id == "stop") return Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => "", TaskScheduler.Default).ContinueWith<string>(_ => throw new OperationCanceledException(ct));
            return Task.FromResult(pick?.Invoke(actions) ?? actions.First(a => a.Primary).Id);
        }
    }

    private static readonly SimOptions Fast = new() { Speed = 0 };

    private static async Task<(Completed Done, AutoRun Run)> RunAlone(IPrepTask task, PrepContext ctx, Func<IReadOnlyList<PrepAction>, string>? pick = null)
    {
        var run = new AutoRun(ctx, pick);
        var outcome = await task.RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout);
        var done = Assert.IsType<Completed>(outcome);
        ctx.Results.Set(done.Result);
        Assert.True((await task.CheckEndStateAsync(ctx, CancellationToken.None)).Ok, $"{task.Id} 끝 상태 약속");
        return (done, run);
    }

    [Fact]
    public async Task 극축_정렬_단독()
    {
        var (done, _) = await RunAlone(new PolarTask(new SimulatedPolarDevices(Fast, new SimFaults())), Harness.Context());
        var r = Assert.IsType<PolarResult>(done.Result);
        Assert.NotNull(r.ErrorArcmin);
    }

    [Fact]
    public async Task 극축_정렬_PHD2가_카메라를_안_놓으면_세_번_시도_후_묻는다()
    {
        var faults = new SimFaults();
        faults.Arm("polar.handover");
        var (_, run) = await RunAlone(new PolarTask(new SimulatedPolarDevices(Fast, faults)), Harness.Context());
        Assert.Contains(run.Statuses, s => s.Contains("(3/3)"));
        Assert.Contains("retry", run.Asked[0]);
    }

    [Fact]
    public async Task 캘리브레이션_단독()
    {
        var (done, _) = await RunAlone(new CalibrationTask(new SimulatedCalibrationDevices(Fast, new SimFaults())), Harness.Context());
        Assert.Equal("A", Assert.IsType<CalibrationResult>(done.Result).Position);
    }

    [Fact]
    public async Task 같은_밤_대상만_바꾸면_캘리브레이션을_재사용할_수_있다()
    {
        var devices = new SimulatedCalibrationDevices(Fast, new SimFaults());
        var memory = new InMemoryPrepMemory();
        await RunAlone(new CalibrationTask(devices), Harness.Context(memory: memory));          // 첫 대상: 새로
        var (done, run) = await RunAlone(new CalibrationTask(devices), Harness.Context(memory: memory)); // 두 번째 대상
        Assert.Equal("reuse,new", run.Asked[0]);
        Assert.True(Assert.IsType<CalibrationResult>(done.Result).Reused);
    }

    [Fact]
    public async Task 대상_이동_단독()
    {
        var (done, _) = await RunAlone(new SlewTask(new SimulatedSlewDevices(Fast, new SimFaults())), Harness.Context());
        Assert.True(Assert.IsType<SlewResult>(done.Result).ArrivalErrorDeg <= SlewTask.ArrivalLimitDeg);
    }

    [Fact]
    public async Task 초점_단독_지난_위치를_기억한다()
    {
        var ctx = Harness.Context();
        var (done, _) = await RunAlone(new FocusTask(new SimulatedFocusDevices(Fast, new SimFaults())), ctx);
        Assert.Equal(12340, Assert.IsType<FocusResult>(done.Result).Position);
        Assert.Equal(12340, ctx.Memory.LastFocus!.Value.Position);
    }

    [Fact]
    public async Task 초점_별이_없으면_넓게_훑고_그래도_없으면_손_초점을_고를_수_있다()
    {
        var faults = new SimFaults();
        faults.Arm("focus.stars");
        var (done, run) = await RunAlone(new FocusTask(new SimulatedFocusDevices(Fast, faults)), Harness.Context(),
            actions => actions.Any(a => a.Id == "manual") ? "manual" : actions.First(a => a.Primary).Id);
        Assert.Contains(run.Statuses, s => s.Contains("넓은 간격"));
        Assert.True(Assert.IsType<FocusResult>(done.Result).Manual);
    }

    [Fact]
    public async Task 포커서가_없으면_손_초점()
    {
        var (done, _) = await RunAlone(new FocusTask(new SimulatedFocusDevices(Fast, new SimFaults())), Harness.Context(hasFocuser: false));
        Assert.True(Assert.IsType<FocusResult>(done.Result).Manual);
    }

    /// <summary>모의 포커서 + 0점 결과를 차례로 돌려주는 장비 (점검에서 "포커서 0점"을 확인한 경우)</summary>
    private sealed class ZeroFocus(IFocusDevices inner, params FocuserZero[] results) : IFocusDevices
    {
        private readonly Queue<FocuserZero> _results = new(results);
        public bool Dropped;
        public int Tries;
        public bool ZeroRequested => !Dropped && _results.Count > 0;
        public void DropZeroRequest() => Dropped = true;
        public Task<FocuserZero> ZeroIfRequestedAsync(CancellationToken ct) { Tries++; return Task.FromResult(_results.Dequeue()); }
        public Task<(int Min, int Max)> LimitsAsync(CancellationToken ct) => inner.LimitsAsync(ct);
        public Task<int> PositionAsync(CancellationToken ct) => inner.PositionAsync(ct);
        public Task<double?> TemperatureAsync(CancellationToken ct) => inner.TemperatureAsync(ct);
        public Task<FocuserMove> MoveAsync(int position, CancellationToken ct) => inner.MoveAsync(position, ct);
        public Task<AutofocusRun> AutofocusAsync(Action<int, double> point, CancellationToken ct) => inner.AutofocusAsync(point, ct);
        public Task<int?> CoarseSearchAsync(int min, int max, int from, Action<int> visiting, CancellationToken ct) => inner.CoarseSearchAsync(min, max, from, visiting, ct);
        public Task<bool> StopAsync(CancellationToken ct) => inner.StopAsync(ct);
        public Task<FocusEndState> ReadEndStateAsync(CancellationToken ct) => inner.ReadEndStateAsync(ct);
    }

    [Fact]
    public async Task 초점_0점을_잡으면_지난_초점_기억을_버리고_진행한다()
    {
        var ctx = Harness.Context();
        ctx.Memory.LastFocus = (30000, 10);
        var dev = new ZeroFocus(new SimulatedFocusDevices(Fast, new SimFaults()), new FocuserZero(true, true));
        var (done, run) = await RunAlone(new FocusTask(dev), ctx);
        Assert.Contains(run.Statuses, s => s.Contains("0점을 잡았습니다"));
        Assert.DoesNotContain(run.Statuses, s => s.Contains("30,000")); // 지난 위치에서 시작하지 않음
        Assert.IsType<FocusResult>(done.Result);
    }

    [Fact]
    public async Task 초점_0점_실패는_다시_시도하고_직접_했다고_하면_진행한다()
    {
        var dev = new ZeroFocus(new SimulatedFocusDevices(Fast, new SimFaults()),
            new FocuserZero(true, false, Problem: "Oasis 포커서를 찾지 못했습니다"), new FocuserZero(true, false, Problem: "Oasis 포커서를 찾지 못했습니다"));
        var asked = 0;
        var (done, run) = await RunAlone(new FocusTask(dev), Harness.Context(),
            actions => actions.Any(a => a.Id == "zero-done") ? (++asked == 1 ? "retry" : "zero-done") : actions.First(a => a.Primary).Id);
        Assert.Equal(2, dev.Tries);
        Assert.True(dev.Dropped);
        Assert.IsType<FocusResult>(done.Result);
    }

    [Fact]
    public async Task 초점_0점을_못_잡는_포커서는_직접_하라고_묻는다()
    {
        var dev = new ZeroFocus(new SimulatedFocusDevices(Fast, new SimFaults()), new FocuserZero(true, false, Unsupported: true));
        var (_, run) = await RunAlone(new FocusTask(dev), Harness.Context());
        Assert.Contains("zero-done,zero-skip", run.Asked);
        Assert.True(dev.Dropped);
    }

    [Fact]
    public async Task 센터링_단독_카메라_방향을_기억한다()
    {
        var ctx = Harness.Context();
        var (done, _) = await RunAlone(new CenterTask(new SimulatedCenterDevices(Fast, new SimFaults())), ctx);
        Assert.Equal(87, Assert.IsType<CenterResult>(done.Result).CameraAngleDeg);
        Assert.Equal(87, ctx.Memory.CameraAngle("m31"));
    }

    [Fact]
    public async Task 센터링_솔빙이_안되면_노출을_늘리고_하늘_전체를_찾은_뒤_초점을_제안한다()
    {
        var faults = new SimFaults();
        faults.Arm("center.solve");
        var run = new AutoRun(Harness.Context(), actions => actions.Any(a => a.Id == "refocus") ? "refocus" : actions[0].Id);
        var outcome = await new CenterTask(new SimulatedCenterDevices(Fast, faults)).RunAsync(run, CancellationToken.None).WaitAsync(Harness.Timeout);
        Assert.Contains(run.Statuses, s => s.Contains("4초"));
        Assert.Contains(run.Statuses, s => s.Contains("하늘 전체"));
        // 초점은 대상 단계의 초점 확인이 비교 없이 바로 다시 맞춘다
        Assert.Equal("focuscheck", Assert.IsType<RedoRequest>(outcome).TaskId);
        Assert.True(run.Context.RefocusRequested);
    }

    [Fact]
    public async Task 가이딩_단독()
    {
        var (done, _) = await RunAlone(new GuidingTask(new SimulatedGuidingDevices(Fast, new SimFaults())), Harness.Context());
        Assert.Equal("충분해요", Assert.IsType<GuidingResult>(done.Result).Grade);
    }

    [Fact]
    public async Task 시험_사진_배경이_밝으면_노출을_줄이고_계획에_반영한다()
    {
        var faults = new SimFaults();
        faults.Arm("test.bright");
        var ctx = Harness.Context();
        var (done, _) = await RunAlone(new TestShotTask(new SimulatedTestShotDevices(Fast, faults)), ctx);
        Assert.Equal(90, ctx.ExposureSeconds);
        Assert.Equal(90, Assert.IsType<TestShotResult>(done.Result).ExposureSeconds);
    }

    [Fact]
    public async Task 시험_사진_내려받기는_한_번_더_시도한다()
    {
        var faults = new SimFaults();
        faults.Arm("test.download");
        var (_, run) = await RunAlone(new TestShotTask(new SimulatedTestShotDevices(Fast, faults)), Harness.Context());
        Assert.Contains(run.Statuses, s => s.Contains("한 번 더"));
        Assert.Contains("retry", run.Asked[0]);
    }
}
