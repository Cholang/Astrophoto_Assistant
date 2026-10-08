using Astro.Server.Assistant;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Prepare.Tasks.FocusCheck;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.Slew;
using Astro.Server.Prepare.Tasks.TestShot;
using Astro.Server.Prepare.Tasks.Wrap;

namespace Astro.Server.Prepare;

/// <summary>
/// 촬영 준비 등록 (docs/PREPARE_IMPLEMENTATION.md). simulate면 모의 장비, 아니면 실제 장비(P3 — Prepare/Real: N.I.N.A.·PHD2·SharpCap·ASCOM).
/// 작업은 장비 인터페이스만 알아서 둘 중 무엇이든 그대로 돈다.
/// </summary>
public static class PrepareSetup
{
    public static IServiceCollection AddPrepare(this IServiceCollection s, bool simulate = true)
    {
        s.AddSingleton<SimOptions>();
        s.AddSingleton<SimFaults>();
        s.AddSingleton(new PrepareMode(simulate));
        s.AddSingleton<Real.Phd2Client>();
        s.AddSingleton<Real.LiveImages>();
        s.AddSingleton<Real.SharpCapBridge>();
        if (simulate)
        {
            s.AddSingleton<IPolarDevices, SimulatedPolarDevices>();
            s.AddSingleton<ICalibrationDevices, SimulatedCalibrationDevices>();
            s.AddSingleton<ISlewDevices, SimulatedSlewDevices>();
            s.AddSingleton<IFocusDevices, SimulatedFocusDevices>();
            s.AddSingleton<ICenterDevices, SimulatedCenterDevices>();
            s.AddSingleton<IGuidingDevices, SimulatedGuidingDevices>();
            s.AddSingleton<ITestShotDevices, SimulatedTestShotDevices>();
            s.AddSingleton<IWrapDevices, SimulatedWrapDevices>();
            s.AddSingleton<Shoot.IShootDevices, Shoot.SimulatedShootDevices>();
        }
        else
        {
            s.AddSingleton<Real.NinaRig>();
            s.AddSingleton<Real.AscomAxis>();
            s.AddSingleton<Real.AscomWeather>();
            s.AddSingleton<Real.ReportAddress>();
            s.AddSingleton<IPolarDevices, Real.RealPolarDevices>();
            s.AddSingleton<ICalibrationDevices, Real.RealCalibrationDevices>();
            s.AddSingleton<ISlewDevices, Real.RealSlewDevices>();
            s.AddSingleton<IFocusDevices, Real.RealFocusDevices>();
            s.AddSingleton<ICenterDevices, Real.RealCenterDevices>();
            s.AddSingleton<IGuidingDevices, Real.RealGuidingDevices>();
            s.AddSingleton<ITestShotDevices, Real.RealTestShotDevices>();
            s.AddSingleton<IWrapDevices, RealWrapDevices>();
            s.AddSingleton<Shoot.IShootDevices, Shoot.RealShootDevices>();
        }
        // 작업 순서 = 등록 순서. 장비 준비(극축 정렬 · 캘리브레이션 · 초점) → 대상(이동 · 센터링 · 초점 확인 · 가이딩 · 시험 사진)
        s.AddSingleton<IPrepTask, PolarTask>();
        s.AddSingleton<IPrepTask, CalibrationTask>();
        s.AddSingleton<IPrepTask, FocusTask>();
        s.AddSingleton<IPrepTask, SlewTask>();
        s.AddSingleton<IPrepTask, CenterTask>();
        s.AddSingleton<IPrepTask, FocusCheckTask>();
        s.AddSingleton<IPrepTask, GuidingTask>();
        s.AddSingleton<IPrepTask, TestShotTask>();
        // 마무리 (플랫 → 다크 → 장비 정리)
        s.AddSingleton<IPrepTask, FlatTask>();
        s.AddSingleton<IPrepTask, DarkTask>();
        s.AddSingleton<IPrepTask, PackTask>();
        s.AddSingleton<Shoot.ShootSession>();
        s.AddSingleton<MountLock>();
        s.AddSingleton<IPrepMemory, InMemoryPrepMemory>();
        s.AddSingleton(sp => new PrepareFlow(sp.GetServices<IPrepTask>(), sp.GetRequiredService<ILogger<PrepareRunner>>(), simulate));
        s.AddSingleton<PrepareStarter>();
        return s;
    }
}

/// <summary>준비 단계가 모의 장비인가 (화면이 모의 그림과 실제 이미지를 고른다)</summary>
public sealed record PrepareMode(bool Simulate);

/// <summary>준비 묶음을 시작한다: 장비 준비는 N.I.N.A. 프로필(관측지·장비)만으로, 대상은 확정 계획·오늘 밤 정보로. 결과 기록은 그날 밤 함께</summary>
public sealed class PrepareStarter(PlanAssistant planner, Sky.TonightService tonight, Engine.EquipmentChoices equipment, PrepareFlow flow, MountLock mount, IPrepMemory memory, Shoot.ShootSession shoot)
{
    /// <summary>장비 준비 시작 (같은 밤이면 이어서). 할 수 없으면 이유(사용자에게 보여 줄 문장)</summary>
    public async Task<string?> StartRigAsync(CancellationToken ct)
    {
        if (await tonight.ReadProfileAsync(ct) is not { } profile) return "N.I.N.A. 프로필을 읽지 못했습니다. N.I.N.A.가 켜져 있는지 확인해 주세요.";
        if (profile.Site is { Latitude: 0, Longitude: 0 })
            return "N.I.N.A. 프로필에 관측지 위치가 없습니다. N.I.N.A.의 옵션 > 일반 > 천문 설정에서 위도·경도를 넣어 주세요.";
        var evening = Sky.TonightService.EveningOf(DateTimeOffset.Now);
        var ctx = new PrepContext(null, profile.Site, profile.Rig.PixelScale is > 0 and var px ? px : 2.0,
            hasGuider: profile.Rig.HasGuider && !equipment.IsWithout("guider"),
            hasFocuser: !equipment.IsWithout("focuser"),
            flow.ResultsFor(evening), mount, memory, () => DateTimeOffset.Now);
        flow.StartRig(ctx, evening);
        return null;
    }

    /// <summary>마무리 시작 (같은 밤이면 이어서). 결과 기록은 그날 밤 것(촬영한 노출·ISO를 다크가 읽는다)</summary>
    public async Task<string?> StartWrapAsync(CancellationToken ct)
    {
        // 찍는 중이거나 가이딩 정지를 확인하지 못했으면 마무리(적도의를 위로)를 시작하지 않는다 (CX-SHOOT-01)
        if (shoot.BlocksNext() is { } blocked) return blocked;
        if (await tonight.ReadProfileAsync(ct) is not { } profile) return "N.I.N.A. 프로필을 읽지 못했습니다. N.I.N.A.가 켜져 있는지 확인해 주세요.";
        var evening = Sky.TonightService.EveningOf(DateTimeOffset.Now);
        var ctx = new PrepContext(null, profile.Site, profile.Rig.PixelScale is > 0 and var px ? px : 2.0,
            hasGuider: profile.Rig.HasGuider && !equipment.IsWithout("guider"),
            hasFocuser: !equipment.IsWithout("focuser"),
            flow.ResultsFor(evening), mount, memory, () => DateTimeOffset.Now);
        flow.StartWrap(ctx, evening);
        return null;
    }

    /// <summary>촬영 시작: 대상 묶음이 끝난(촬영 시작을 누른) 상황으로</summary>
    public string? StartShoot(Shoot.ShootSession shoot)
    {
        if (flow.Target.Context is not { } ctx || !flow.Target.AllDone) return "대상 준비가 끝나지 않았습니다. 시험 사진까지 마친 뒤 촬영을 시작해 주세요.";
        shoot.Start(ctx);
        return null;
    }

    /// <summary>대상 시작 (같은 계획이면 이어서). 할 수 없으면 이유</summary>
    public string? StartTarget()
    {
        if (shoot.BlocksNext() is { } blocked) return blocked;
        if (planner.Confirmed is not { } plan) return "확정된 계획이 없습니다. 계획에서 \"이 대상으로 이동\"을 눌러 주세요.";
        if (planner.Night is not { } night) return "오늘 밤 정보를 읽지 못했습니다. 계획 화면을 다시 열어 주세요.";
        var ctx = new PrepContext(plan, night.Site, night.Rig.PixelScale is > 0 and var px ? px : 2.0,
            hasGuider: night.Rig.HasGuider && !equipment.IsWithout("guider"),
            hasFocuser: !equipment.IsWithout("focuser"),
            flow.ResultsFor(Sky.TonightService.EveningOf(DateTimeOffset.Now)), mount, memory, () => DateTimeOffset.Now);
        return flow.StartTarget(ctx);
    }
}
