using Astro.Core.Sky;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Tasks.Slew;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ③ 대상 이동 실장비: N.I.N.A. slew(ra는 도) → 남은 거리를 1초마다 알림 → 도착하면 추적을 항성으로 확인.
/// 멈춤은 slew/stop + 멈춘 것 확인 (2026-10-06 실기: 감속 후 정지, N.I.N.A. 정보는 2초마다 갱신).
/// </summary>
public sealed class RealSlewDevices(NinaRig rig) : ISlewDevices
{
    public double TargetAltitude(PrepContext ctx) => ctx.TargetAltitude(ctx.Now());

    public int? MinutesUntilUsable(PrepContext ctx)
    {
        var now = ctx.Now();
        for (var m = 0; m <= 12 * 60; m += 1)
            if (ctx.TargetAltitude(now.AddMinutes(m)) >= SlewTask.UsableAltitude) return m;
        return null;
    }

    public Task WaitTickAsync(CancellationToken ct) => Task.Delay(TimeSpan.FromSeconds(30), ct);

    public async Task<SlewMove> SlewToTargetAsync(double raDeg, double decDeg, Action<double> remainingDeg, CancellationToken ct)
    {
        if (await rig.StartSlewAsync(raDeg, decDeg, ct) is { } problem) return new(false, 0, problem);
        if (await rig.WaitSlewAsync(raDeg, decDeg, remainingDeg, TimeSpan.FromMinutes(5), ct) is not { } d)
            return new(false, 0, "적도의가 응답하지 않거나 이동이 끝나지 않았습니다");
        if (!(await rig.MountAsync(ct))?.Tracking ?? true) await rig.SetTrackingAsync(true, ct);
        return new(true, d);
    }

    public Task<bool> StopAsync(CancellationToken ct) => rig.StopSlewAndConfirmAsync(ct);

    public async Task<SlewEndState> ReadEndStateAsync(PrepContext ctx, CancellationToken ct)
    {
        if (await rig.MountAsync(ct) is not { } m) return new(double.MaxValue, true, false);
        var d = Astronomy.Separation(new Equatorial(m.RaDeg, m.DecDeg), new Equatorial(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees));
        return new(d, m.Slewing, m.Tracking);
    }
}
