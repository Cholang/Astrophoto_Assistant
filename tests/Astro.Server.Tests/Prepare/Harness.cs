using Astro.Core.Sky;
using Astro.Server.Assistant;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>준비 시험 도구: 모의 장비(기다리지 않음)로 러너·작업을 만들고, 상태가 원하는 대로 될 때까지 기다린다</summary>
public static class Harness
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static PreparationPlan Plan(DateTimeOffset? confirmed = null) => new(
        "m31", "M31", "안드로메다", 10.68, 41.27, "", null, 120, 800, "None", EndRule.TargetLowOrDawn,
        DateTimeOffset.Now, DateTimeOffset.Now.AddHours(5), 100, true, CalibrationFrames.None, true, confirmed ?? DateTimeOffset.Now);

    public static PrepContext Context(bool hasGuider = true, bool hasFocuser = true, IPrepMemory? memory = null, PreparationPlan? plan = null) =>
        new(plan ?? Plan(), new Site(37.82, 127.14), 2.41, hasGuider, hasFocuser, new PrepResults(), new MountLock(),
            memory ?? new InMemoryPrepMemory(), () => DateTimeOffset.Now);

    public sealed record Sim(SimOptions Options, SimFaults Faults);

    public static (PrepareRunner Runner, Sim Sim) RealTasks()
    {
        var sim = new Sim(new SimOptions { Speed = 0 }, new SimFaults());
        IPrepTask[] tasks =
        [
            new PolarTask(new SimulatedPolarDevices(sim.Options, sim.Faults)),
            new CalibrationTask(new SimulatedCalibrationDevices(sim.Options, sim.Faults)),
            new SlewTask(new SimulatedSlewDevices(sim.Options, sim.Faults)),
            new FocusTask(new SimulatedFocusDevices(sim.Options, sim.Faults)),
            new CenterTask(new SimulatedCenterDevices(sim.Options, sim.Faults)),
            new GuidingTask(new SimulatedGuidingDevices(sim.Options, sim.Faults)),
            new TestShotTask(new SimulatedTestShotDevices(sim.Options, sim.Faults)),
        ];
        return (new PrepareRunner(tasks, NullLogger<PrepareRunner>.Instance), sim);
    }

    /// <summary>조건이 맞을 때까지 기다린다 (시간 초과면 마지막 상태와 함께 실패)</summary>
    public static async Task<PrepView> Until(this PrepareRunner runner, Func<PrepView, bool> cond, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var wait = runner.NextChangeAsync();
            var v = runner.View();
            if (cond(v)) return v;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{what} — 마지막 상태: {Describe(v)}");
            await Task.WhenAny(wait, Task.Delay(200));
        }
    }

    public static PrepTaskStatus Row(this PrepView v, string id) => v.Tasks.Single(t => t.Id == id).Status;

    public static bool Has(this PrepView v, string action) => v.Current?.Center.Actions.Any(a => a.Id == action) == true;

    /// <summary>그 버튼이 보이면 누른다</summary>
    public static async Task Press(this PrepareRunner runner, string action)
    {
        await runner.Until(v => v.Has(action), $"버튼 {action}");
        Assert.Null(runner.Act(action));
    }

    /// <summary>준비를 끝까지: 보이는 주 버튼(정렬 완료·다음·촬영 시작 등)을 차례로 누른다</summary>
    public static async Task<PrepView> RunToReady(this PrepareRunner runner, int maxPresses = 60)
    {
        for (var i = 0; i < maxPresses; i++)
        {
            var v = await runner.Until(x => x.Ready || x.Current?.Center.Actions.Count > 0, "버튼 또는 준비 끝");
            if (v.Ready) return v;
            var primary = v.Current!.Center.Actions.First(a => a.Primary);
            if (primary.Id == "stop") { await Task.WhenAny(runner.NextChangeAsync(), Task.Delay(200)); continue; } // 멈춤은 누르지 않는다
            Assert.Null(runner.Act(primary.Id));
            await Task.Delay(5);
        }
        throw new InvalidOperationException("준비가 끝나지 않았습니다: " + Describe(runner.View()));
    }

    public static string Describe(PrepView v) =>
        string.Join(", ", v.Tasks.Select(t => $"{t.Id}:{t.Status}")) +
        $" | 지금 {v.Current?.TaskId} '{v.Current?.Guide.Title}' 상태 '{v.Current?.Center.Status?.Text}' 버튼 [{string.Join(",", v.Current?.Center.Actions.Select(a => a.Id) ?? [])}]";
}
