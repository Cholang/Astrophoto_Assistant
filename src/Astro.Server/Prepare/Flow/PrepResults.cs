namespace Astro.Server.Prepare.Flow;

// 작업 결과 기록 — 작업 사이에 주고받는 유일한 것 (docs/PREPARE_IMPLEMENTATION.md 2.3).
// 모양을 바꾸면 여러 작업이 영향을 받으므로 따로 리뷰한다. 테스트(ResultContractTests)가 모양을 고정한다.

/// <summary>① 극축 정렬. 환산을 확인하기 전(실제 모드)엔 ErrorArcmin이 null이고 픽셀 값만</summary>
public sealed record PolarResult(double? ErrorArcmin, double ErrorXPx, double ErrorYPx, string? Grade, DateTimeOffset At);

/// <summary>② 캘리브레이션. Evening = 그날 밤(같은 밤 대상만 바꾸면 재사용)</summary>
public sealed record CalibrationResult(bool Reused, double? OrthogonalityErrorDeg, string Position, DateOnly Evening, DateTimeOffset At);

/// <summary>③ 대상 이동</summary>
public sealed record SlewResult(double ArrivalErrorDeg, double AltitudeDeg, DateTimeOffset At);

/// <summary>④ 초점. Manual = 손으로 맞춤(위치·HFR 모름)</summary>
public sealed record FocusResult(bool Manual, int? Position, double? Hfr, double? TemperatureC, DateTimeOffset At);

/// <summary>⑤ 센터링</summary>
public sealed record CenterResult(double ErrorArcmin, double? CameraAngleDeg, int Attempts, DateTimeOffset At);

/// <summary>⑥ 가이딩 (RMS, ″)</summary>
public sealed record GuidingResult(double TotalArcsec, double RaArcsec, double DecArcsec, string Grade, DateTimeOffset At);

/// <summary>⑦ 시험 사진</summary>
public sealed record TestShotResult(int ExposureSeconds, double? Hfr, double? Eccentricity, double? SaturatedPercent, string? FilePath, DateTimeOffset At);

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
