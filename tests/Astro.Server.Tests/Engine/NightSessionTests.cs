using Astro.Server.Assistant;
using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Session;
using Astro.Server.Shoot;
using Astro.Server.Tests.Prepare;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Engine;

/// <summary>그날 밤 진행 기록 — AA가 중간에 꺼졌다 다시 켰을 때 (2026-10-08 사용자 결정)</summary>
public class NightSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aa-session-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 8, 22, 40, 0, TimeSpan.FromHours(9));
    private DateTimeOffset _boot = new(2026, 10, 8, 18, 0, 0, TimeSpan.FromHours(9));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private NightSessionStore Store() => new(_dir, () => _now, () => _boot);

    /// <summary>촬영 중 기록 하나를 파일에 남긴다 (M31 47장)</summary>
    private SessionSnapshot Saved(string phase = "shoot", bool closed = false)
    {
        var plan = Harness.Plan() with { Start = _now.AddHours(-2), End = _now.AddHours(4), EstimatedFrames = 182 };
        var row = new TargetShots("m31", "M31 안드로메다", 47, 3, 120, 800, new Dictionary<string, int> { ["A"] = 47, ["F"] = 3 }, "D:/Astro/M31",
            PlanKey: NightSessionStore.PlanKeyOf(plan));
        var snap = new SessionSnapshot(Sky.TonightService.EveningOf(_now), "p1", phase, _now, _boot, closed, plan, null,
            new NightShootResult([row]), null, null, new FocusResult(false, 25000, 2.0, 12, _now), false, 25000, 12, null, null);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "session.json"), System.Text.Json.JsonSerializer.Serialize(snap, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        return snap;
    }

    [Fact]
    public void 앱만_꺼졌다_곧_켜면_이어서_할지_묻는다()
    {
        Saved();
        _now = _now.AddMinutes(10);
        var p = Store().Pending("p1");
        Assert.Equal("resume", p!.Kind);
        Assert.Equal("target", p.Phase); // 촬영 중이었으면 대상(이동부터)
        Assert.Contains("M31", p.Summary);
        Assert.Contains("47/182", p.Summary);
    }

    [Fact]
    public void 컴퓨터가_꺼졌던_것이면_알리기만_한다()
    {
        Saved();
        _now = _now.AddMinutes(10);
        _boot = _now.AddMinutes(-3); // 그 사이 다시 켜짐
        var p = Store().Pending("p1");
        Assert.Equal("notice", p!.Kind);
        Assert.Contains("컴퓨터가 꺼져", p.Summary);
    }

    [Fact]
    public void 두_시간이_지나면_알리기만_하고_며칠_뒤에는_묻지도_않는다()
    {
        Saved();
        _now = _now.AddHours(3);
        Assert.Equal("notice", Store().Pending("p1")!.Kind);
        _now = _now.AddDays(2);
        Assert.Null(Store().Pending("p1"));
    }

    [Fact]
    public void 끝낸_밤이나_다른_프로필이면_이어서_묻지_않는다()
    {
        Saved(closed: true);
        Assert.Null(Store().Pending("p1"));
        Saved();
        Assert.Equal("notice", Store().Pending("other")!.Kind);
    }

    [Fact]
    public void 새로_시작하면_다시_묻지_않는다()
    {
        Saved();
        var store = Store();
        store.Dismiss();
        Assert.Null(store.Pending("p1"));
    }

    [Fact]
    public async Task 이어서_찍으면_전에_찍은_장수부터_센다()
    {
        var snap = Saved();
        var results = new PrepResults();
        var memory = new InMemoryPrepMemory();
        // 계획을 되살리는 부분은 PlanAssistant가 N.I.N.A.를 읽어야 해서 여기서는 기록·기억만 (계획은 직접)
        var restored = snap with { Plan = null };
        File.WriteAllText(Path.Combine(_dir, "session.json"), System.Text.Json.JsonSerializer.Serialize(restored, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.Equal("shoot", (await Store().RestoreAsync(null, results, memory, CancellationToken.None))!.Phase);
        Assert.Equal(25000, memory.LastFocus!.Value.Position);

        var sim = new SimOptions { Speed = 0 };
        var session = new ShootSession(new SimulatedShootDevices(sim, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        // 촬영은 실제 시계로 끝을 본다 → 끝 시각도 지금 기준 (고정 날짜면 그 시각이 지난 뒤 바로 끝남 — 2026-10-09 03시에 실패)
        var ctx = Harness.TargetContext(results, memory, snap.Plan! with { EstimatedFrames = 49, ExposureSeconds = 30, Start = DateTimeOffset.Now.AddHours(-1), End = DateTimeOffset.Now.AddHours(4) });
        session.Start(ctx);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (session.View().Mode != ShootMode.Ended && DateTime.UtcNow < deadline) await session.NextChangeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var v = session.View();
        Assert.Equal(49, v.Good); // 47장에서 이어서 2장 더
        var row = Assert.Single(results.Get<NightShootResult>()!.Targets); // 같은 줄에 이어서
        Assert.Equal(49, row.Good);
    }
}
