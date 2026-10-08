namespace Astro.Server.Prepare.Flow;

// 작업 결과 기록 — 작업 사이에 주고받는 유일한 것 (docs/PREPARE_IMPLEMENTATION.md 2.3).
// 모양을 바꾸면 여러 작업이 영향을 받으므로 따로 리뷰한다. 테스트(ResultContractTests)가 모양을 고정한다.
// Skipped = 장비 문제로 사용자가 이 작업을 건너뜀 (2026-10-08 사용자 결정 — 한 작업 실패로 밤이 막히지 않게). 값은 비어 있다

/// <summary>① 극축 정렬. 환산을 확인하기 전(실제 모드)엔 ErrorArcmin이 null이고 픽셀 값만</summary>
public sealed record PolarResult(double? ErrorArcmin, double ErrorXPx, double ErrorYPx, string? Grade, DateTimeOffset At, bool Skipped = false);

/// <summary>② 캘리브레이션. Evening = 그날 밤(같은 밤 대상만 바꾸면 재사용)</summary>
public sealed record CalibrationResult(bool Reused, double? OrthogonalityErrorDeg, string Position, DateOnly Evening, DateTimeOffset At, bool Skipped = false);

/// <summary>③ 대상 이동. AcceptedHere = 이동이 안 돼 "지금 위치를 목표로" 인정함 (센터링도 건너뜀)</summary>
public sealed record SlewResult(double ArrivalErrorDeg, double AltitudeDeg, DateTimeOffset At, bool AcceptedHere = false);

/// <summary>④ 초점. Manual = 손으로 맞춤(위치·HFR 모름)</summary>
public sealed record FocusResult(bool Manual, int? Position, double? Hfr, double? TemperatureC, DateTimeOffset At);

/// <summary>⑤ 센터링. Hfr·Stars = 마지막 센터링 사진의 별 크기·별 수 (초점 확인이 읽음, 못 재면 null)</summary>
public sealed record CenterResult(double ErrorArcmin, double? CameraAngleDeg, int Attempts, DateTimeOffset At, double? Hfr = null, int? Stars = null, bool Skipped = false);

/// <summary>대상 묶음의 초점 확인. Refocused면 Position·Hfr·TemperatureC가 새로 맞춘 값 (시험 사진이 초점 결과보다 이것을 먼저 읽음)</summary>
public sealed record FocusCheckResult(bool Refocused, double? TemperatureChangeC, double? CenterHfr, int? Position, double? Hfr, double? TemperatureC, DateTimeOffset At);

/// <summary>⑥ 가이딩 (RMS, ″)</summary>
public sealed record GuidingResult(double TotalArcsec, double RaArcsec, double DecArcsec, string Grade, DateTimeOffset At, bool Skipped = false);

/// <summary>그날 밤 가이딩 없이 진행 (캘리브레이션·가이딩을 건너뜀). 있으면 PrepContext.HasGuider = false — 장비 준비를 다시 시작하면 지운다</summary>
public sealed record GuiderSkipped(string Why, DateTimeOffset At);

/// <summary>⑦ 시험 사진</summary>
public sealed record TestShotResult(int ExposureSeconds, double? Hfr, double? Eccentricity, double? SaturatedPercent, string? FilePath, DateTimeOffset At, bool Skipped = false);

/// <summary>이번 준비의 결과 기록 모음. 작업은 앞 작업의 기록을 타입으로 읽는다</summary>
public sealed class PrepResults
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, object> _byType = [];

    public T? Get<T>() where T : class
    {
        lock (_gate) return _byType.TryGetValue(typeof(T), out var v) ? (T)v : null;
    }

    public void Set(object result)
    {
        lock (_gate) _byType[result.GetType()] = result;
    }

    public void Remove(Type type)
    {
        lock (_gate) _byType.Remove(type);
    }
}
