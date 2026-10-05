using Astro.Server.Prepare.Flow;

namespace Astro.Server.Tests.Prepare;

/// <summary>러너 시험용 가짜 작업: 정해진 결과를 내고, 끝 상태·정지 확인을 조절할 수 있다. 일어난 일을 Log에 남긴다</summary>
public sealed class FakeTask(string id, List<string> log) : IPrepTask
{
    public string Id => id;
    public string Title => id;
    public string StartLabel => $"{id} 시작";
    public IReadOnlyList<SubStep> SubSteps { get; } = [new("a", "가"), new("b", "나")];

    /// <summary>true면 RunAsync가 취소될 때까지 끝나지 않는다 (움직이는 장비 흉내)</summary>
    public bool Hang { get; set; }
    /// <summary>true면 취소돼도 무시하고 계속 화면을 갱신하려 한다 (늦은 갱신 흉내)</summary>
    public bool IgnoreCancel { get; set; }
    public bool EndStateOk { get; set; } = true;
    public bool StopConfirms { get; set; } = true;
    public ITaskRun? LastRun { get; private set; }

    public bool AppliesTo(PrepContext ctx) => true;

    public async Task<TaskOutcome> RunAsync(ITaskRun run, CancellationToken ct)
    {
        LastRun = run;
        lock (log) log.Add($"run:{id}:{run.RunId}");
        run.Guide(id, "시험");
        run.Readout("x", "1", "값", Tone.Busy, live: true);
        if (Hang)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) when (IgnoreCancel) { }
        }
        return new Completed(new object(), $"{id} 끝", $"{id} 좋아요", "다음으로");
    }

    public Task<EndStateCheck> CheckEndStateAsync(PrepContext ctx, CancellationToken ct) =>
        Task.FromResult(EndStateOk ? EndStateCheck.Pass : EndStateCheck.Fail("약속 어김"));

    public Task<bool> StopAsync(CancellationToken ct)
    {
        lock (log) log.Add($"stop:{id}:{StopConfirms}");
        return Task.FromResult(StopConfirms);
    }
}
