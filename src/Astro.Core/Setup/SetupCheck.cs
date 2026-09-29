namespace Astro.Core.Setup;

/// <summary>0단계 기본 환경 체크 항목 하나의 결과.</summary>
public enum CheckStatus
{
    /// <summary>확인 중</summary>
    Running,
    Pass,
    Fail,
    /// <summary>권장 항목 실패. 건너뛰고 계속할 수 있다.</summary>
    Warn,
    /// <summary>앞 단계가 해결돼야 확인할 수 있다.</summary>
    Skipped,
}

public enum CheckSeverity
{
    /// <summary>실패하면 다음 화면으로 넘어갈 수 없다.</summary>
    Required,
    /// <summary>실패해도 "이 기능 없이 계속하기"가 가능하다.</summary>
    Recommended,
}

/// <summary>
/// 실패·경고일 때 진단 카드에 보여 줄 내용 (DESIGN.md 4장: 무슨 일 / 왜 / 이렇게 / 원본).
/// </summary>
public sealed record Diagnosis(
    IReadOnlyList<string> Causes,
    string Fix,
    string? ActionLabel = null,
    string? ActionUrl = null,
    string? Detail = null);

/// <param name="Title">한글 이름</param>
/// <param name="Term">원어 표기 (예: "Plate Solving"). 한글 이름 옆에 괄호로 보인다.</param>
/// <param name="Hint">용어 한 줄 설명 (물음표 툴팁)</param>
/// <param name="Message">결과 한 문장. 진단 카드의 "무슨 일이 있었나요"로도 쓴다.</param>
public sealed record CheckResult(
    string Id,
    string Title,
    string? Term,
    string? Hint,
    CheckSeverity Severity,
    CheckStatus Status,
    string Message,
    Diagnosis? Diagnosis = null);

public static class SetupReport
{
    /// <summary>필수 항목이 모두 통과했는지. 권장 항목은 보지 않는다.</summary>
    public static bool CanContinue(IEnumerable<CheckResult> results) =>
        results.All(r => r.Severity != CheckSeverity.Required || r.Status == CheckStatus.Pass);
}
