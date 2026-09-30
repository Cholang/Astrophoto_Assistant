namespace Astro.Server.Assistant;

// 촬영 계획 네 칸 (DESIGN.md 3장 "촬영 계획 화면"): 대상 / 구도 / 촬영 설정 / 끝난 뒤.
// 칸 상태: empty(아직) · asking(지금 묻는 중) · set(정해짐). 화면은 이 모양을 그대로 그린다.

public sealed class ShootingPlan
{
    public PlanTarget? Target { get; set; }
    public PlanFraming? Framing { get; set; }
    public PlanSettings? Settings { get; set; }
    public PlanAfter? After { get; set; }

    /// <summary>지금 묻는 칸: target · framing · settings · after</summary>
    public string? Asking { get; set; }

    public bool Complete => Target is not null && Framing is not null && Settings is not null && After is not null;
}

public sealed record PlanTarget(
    string Id, string Name, string? CommonName, string? KoreanName, string Type, string Constellation,
    double? SizeArcmin, double? Magnitude,
    DateTimeOffset? UsableStart, DateTimeOffset? UsableEnd,
    DateTimeOffset HighestAt, double HighestAltitude, DateTimeOffset? Transit, double MoonSeparation);

public sealed record PlanFraming(
    /// <summary>가운데 · 조금 비켜서 등 (AI가 사용자 말로 적음)</summary>
    string Placement,
    double RotationDegrees,
    /// <summary>대상이 화면 긴 변에서 차지하는 비율(%)</summary>
    double? FillPercent);

public sealed record PlanSettings(
    int ExposureSeconds, int Iso, string Filter,
    /// <summary>화면용 문장: "대상이 낮아지거나 새벽이 올 때까지" 등</summary>
    string EndCondition,
    DateTimeOffset Start, DateTimeOffset End, int EstimatedFrames,
    bool ExposureRecommended, bool IsoRecommended);

public sealed record PlanAfter(string Mount, string Calibration, string Equipment);
