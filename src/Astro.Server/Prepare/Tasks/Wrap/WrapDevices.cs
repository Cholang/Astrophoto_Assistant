using System.Diagnostics;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.Wrap;

/// <summary>마무리 모의 장비. "wrap.bright" = 패널이 너무 밝음(노출이 셔터 한계보다 짧음, 한 번)</summary>
public sealed class SimulatedWrapDevices(SimOptions sim, SimFaults faults) : IWrapDevices
{
    public async Task<bool> PointUpAsync(CancellationToken ct) { await sim.Delay(1600, ct); return true; }

    public async Task<FlatExposure> FindFlatExposureAsync(int iso, Action<double, double> trying, CancellationToken ct)
    {
        if (faults.Take("wrap.bright"))
        {
            trying(0.01, 92);
            await sim.Delay(600, ct);
            return new FlatExposure(false, 0.004, 92, TooShort: true, Problem: "노출이 1/250초보다 짧아져요");
        }
        foreach (var (s, m) in new[] { (1.0, 64.0), (0.8, 51.0) })
        {
            trying(s, m);
            await sim.Delay(600, ct);
        }
        return new FlatExposure(true, 0.8, 51);
    }

    public async Task<bool> CaptureAsync(string imageType, double seconds, int iso, CancellationToken ct)
    {
        await sim.Delay(imageType == "DARK" ? 250 : 70, ct);
        return true;
    }

    public async Task<bool> HomeAsync(CancellationToken ct) { await sim.Delay(2000, ct); return true; }
    public async Task<bool> TrackingOffAsync(CancellationToken ct) { await sim.Delay(300, ct); return true; }
    public async Task<bool> DisconnectAllAsync(CancellationToken ct) { await sim.Delay(800, ct); return true; }
    public async Task<bool> CloseProgramsAsync(CancellationToken ct) { await sim.Delay(800, ct); return true; }
    public async Task<bool> StopAsync(CancellationToken ct) { await sim.Delay(50, ct); return !faults.Take("stop.fail"); }
}

/// <summary>
/// 마무리 실장비 (2026-10-07 — 실기 미확인). 위쪽 = 지금 항성시의 자오선·고도 약 80°(적위 = 위도 − 10°).
/// 플랫 노출은 AA가 찾는다: 저장하지 않는 사진으로 평균 밝기를 재며 50% 근처까지 (카메라 비트 수 기준, 없으면 14비트 — X-T5).
/// 1/250초보다 짧으면 셔터 그림자 위험으로 보고 패널을 어둡게 하자고 한다. 프로그램 닫기는 창 닫기(강제 종료 없음).
/// </summary>
public sealed class RealWrapDevices(NinaRig rig, ILogger<RealWrapDevices> log) : IWrapDevices
{
    public const double MinFlatSeconds = 1.0 / 250, MaxFlatSeconds = 10;

    public async Task<bool> PointUpAsync(CancellationToken ct)
    {
        if (await rig.MountAsync(ct) is not { } m) return false;
        var ra = m.SiderealHours * 15;
        var dec = m.Site.Latitude - 10;
        if (await rig.StartSlewAsync(ra, dec, ct) is not null) return false;
        return await rig.WaitSlewAsync(ra, dec, null, TimeSpan.FromMinutes(3), ct) is < 2;
    }

    public async Task<FlatExposure> FindFlatExposureAsync(int iso, Action<double, double> trying, CancellationToken ct)
    {
        var bits = await rig.CameraBitDepthAsync(ct) ?? 14;
        var full = Math.Pow(2, bits) - 1;
        var s = 1.0;
        for (var i = 0; i < 8; i++)
        {
            var shot = await rig.CaptureAsync(s, solve: false, save: false, null, ct, gain: iso, imageType: "SNAPSHOT");
            if (!shot.Ok) return new FlatExposure(false, s, 0, Problem: shot.Problem);
            if (await rig.LastStatsAsync(ct) is not { } st || !double.IsFinite(st.Mean)) return new FlatExposure(false, s, 0, Problem: "사진 밝기를 읽지 못했습니다");
            var pct = Math.Min(100, st.Mean / full * 100);
            trying(s, pct);
            if (pct is >= 40 and <= 60) return new FlatExposure(true, s, pct);
            var next = pct <= 0.5 ? s * 8 : s * 50 / pct;
            if (next < MinFlatSeconds) return new FlatExposure(false, next, pct, TooShort: true, Problem: "노출이 1/250초보다 짧아져요");
            if (next > MaxFlatSeconds) return new FlatExposure(false, next, pct, TooLong: true, Problem: "노출이 10초보다 길어져요");
            s = Math.Round(next, 4);
        }
        return new FlatExposure(false, s, 0, Problem: "밝기가 목표에 맞지 않습니다");
    }

    public async Task<bool> CaptureAsync(string imageType, double seconds, int iso, CancellationToken ct) =>
        (await rig.CaptureAsync(seconds, solve: false, save: true, null, ct, gain: iso, imageType: imageType)).Ok; // gain = ISO (X-T5)

    public Task<bool> HomeAsync(CancellationToken ct) => rig.HomeAsync(ct);
    public Task<bool> TrackingOffAsync(CancellationToken ct) => rig.SetTrackingAsync(false, ct);

    public async Task<bool> DisconnectAllAsync(CancellationToken ct)
    {
        var ok = true;
        // 적도의는 마지막에 (홈·추적 끔을 마친 뒤)
        foreach (var d in new[] { "guider", "camera", "focuser", "filterwheel", "rotator", "flatdevice", "switch", "mount" })
        {
            var done = await rig.DisconnectAsync(d, ct);
            if (!done && d is "camera" or "mount") ok = false;
        }
        return ok;
    }

    public async Task<bool> CloseProgramsAsync(CancellationToken ct)
    {
        var all = true;
        foreach (var name in new[] { "phd2", "NINA" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.CloseMainWindow();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { all = false; log.LogWarning("{Name}이 닫히지 않음 (저장 확인 창 등)", name); }
                }
                catch (InvalidOperationException) { /* 이미 닫힘 */ }
                finally { p.Dispose(); }
            }
        return all;
    }

    public async Task<bool> StopAsync(CancellationToken ct) =>
        await rig.AbortExposureAsync(ct) & await rig.StopSlewAndConfirmAsync(ct);
}
