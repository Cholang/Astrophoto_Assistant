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

namespace Astro.Server.Prepare;

/// <summary>촬영 준비 등록 (docs/PREPARE_IMPLEMENTATION.md). 지금은 모의 장비만 — 실제 장비 어댑터는 P3에서 작업별 인터페이스로 채운다</summary>
public static class PrepareSetup
{
    public static IServiceCollection AddPrepare(this IServiceCollection s)
    {
        s.AddSingleton<SimOptions>();
        s.AddSingleton<SimFaults>();
        s.AddSingleton<IPolarDevices, SimulatedPolarDevices>();
        s.AddSingleton<ICalibrationDevices, SimulatedCalibrationDevices>();
        s.AddSingleton<ISlewDevices, SimulatedSlewDevices>();
        s.AddSingleton<IFocusDevices, SimulatedFocusDevices>();
        s.AddSingleton<ICenterDevices, SimulatedCenterDevices>();
        s.AddSingleton<IGuidingDevices, SimulatedGuidingDevices>();
        s.AddSingleton<ITestShotDevices, SimulatedTestShotDevices>();
        // 작업 순서 = 등록 순서
        s.AddSingleton<IPrepTask, PolarTask>();
        s.AddSingleton<IPrepTask, CalibrationTask>();
        s.AddSingleton<IPrepTask, SlewTask>();
        s.AddSingleton<IPrepTask, FocusTask>();
        s.AddSingleton<IPrepTask, CenterTask>();
        s.AddSingleton<IPrepTask, GuidingTask>();
        s.AddSingleton<IPrepTask, TestShotTask>();
        s.AddSingleton<MountLock>();
        s.AddSingleton<IPrepMemory, InMemoryPrepMemory>();
        s.AddSingleton<PrepareRunner>();
        s.AddSingleton<PrepareStarter>();
        return s;
    }
}

/// <summary>확정 계획·오늘 밤 정보·장비 선택으로 준비 상황(PrepContext)을 만들어 러너를 시작한다</summary>
public sealed class PrepareStarter(PlanAssistant planner, Engine.EquipmentChoices equipment, PrepareRunner runner, MountLock mount, IPrepMemory memory)
{
    /// <summary>준비 시작. 할 수 없으면 이유(사용자에게 보여 줄 문장)</summary>
    public string? Start()
    {
        if (planner.Confirmed is not { } plan) return "확정된 계획이 없습니다. 촬영 계획에서 \"이 계획으로 준비 시작\"을 눌러 주세요.";
        if (planner.Night is not { } night) return "오늘 밤 정보를 읽지 못했습니다. 촬영 계획 화면을 다시 열어 주세요.";
        var ctx = new PrepContext(plan, night.Site, night.Rig.PixelScale is > 0 and var px ? px : 2.0,
            hasGuider: night.Rig.HasGuider && !equipment.IsWithout("guider"),
            hasFocuser: !equipment.IsWithout("focuser"),
            new PrepResults(), mount, memory, () => DateTimeOffset.Now);
        runner.Start(ctx);
        return null;
    }
}
