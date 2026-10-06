using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>
/// 촬영 장비 (DESIGN.md "촬영"). 실제: N.I.N.A. Advanced API(촬영·통계·자동초점·적도의) + PHD2(디더링·가이딩 상태).
/// 세션(ShootSession)이 순서를 정하고, 장비는 한 가지 일씩만 한다.
/// </summary>
public interface IShootDevices
{
    /// <summary>한 장 찍어 저장한다 (라이트). 노출 중 지난 초를 알린다</summary>
    Task<FrameShot> ExposeAsync(int seconds, int iso, Action<int> elapsed, CancellationToken ct);
    /// <summary>지금 가이딩 오차(″). 모르면 null</summary>
    Task<double?> GuideRmsAsync(CancellationToken ct);
    /// <summary>가이딩 신호: 연결·가이딩 중·적도의 추적·최근 SNR·HFD·튐·이슬 여유 (GuideWatch가 원인을 가린다)</summary>
    Task<GuideRaw> GuideRawAsync(CancellationToken ct);
    /// <summary>지금 가이드 노출(ms)</summary>
    int GuideExposureMs { get; }
    /// <summary>가이드 노출 바꾸기 (PHD2 목록 값만 — 2026-10-07 실기: 1.2초는 거절, 1·1.5·2초…는 됨, 루프 중에도 됨)</summary>
    Task<bool> SetGuideExposureAsync(int ms, CancellationToken ct);
    /// <summary>가이드 별 다시 고르기</summary>
    Task<bool> ReselectStarAsync(CancellationToken ct);
    /// <summary>가이드 카메라·PHD2 다시 연결</summary>
    Task<bool> ReconnectGuiderAsync(CancellationToken ct);
    /// <summary>솔빙으로 다시 가운데 (오래 멈췄다 돌아왔을 때·크게 튀었을 때)</summary>
    Task<bool> RecenterAsync(PrepContext ctx, CancellationToken ct);
    /// <summary>지금 노출 멈추기 (그 장은 버림)</summary>
    Task<bool> AbortExposureAsync(CancellationToken ct);
    /// <summary>이슬 열선 올리기. 열선을 다룰 수 없으면 false (알림만)</summary>
    Task<bool> DewHeaterBoostAsync(CancellationToken ct);
    Task<bool> ResumeGuidingAsync(CancellationToken ct);
    /// <summary>가이딩을 멈추고 멈춘 것을 확인한다 — PHD2가 Stopped·Looping일 때만 true (LostLock·조회 실패는 멈춤이 아님, CX-SHOOT-01)</summary>
    Task<bool> StopGuidingAsync(CancellationToken ct);
    /// <summary>디더링하고 가이딩이 안정될 때까지</summary>
    Task<bool> DitherAsync(CancellationToken ct);
    /// <summary>자오선 반전: 0 반전 → 1 다시 센터링 → 2 가이딩 재시작. 단계가 바뀔 때마다 알린다. 단계별 결과를 따로 (CX-SHOOT-02)</summary>
    Task<FlipOutcome> FlipAsync(PrepContext ctx, Action<int> step, CancellationToken ct);
    /// <summary>자동초점 (지점마다 위치·별 크기를 알린다)</summary>
    Task<RefocusOutcome> RefocusAsync(Action<int, double> point, CancellationToken ct);
    Task<double?> TemperatureAsync(CancellationToken ct);
    /// <summary>실패한 사진을 같은 폴더 아래 "제외" 폴더로 옮긴다. 옮긴 경로 (못 옮기면 null)</summary>
    string? MoveToExcluded(string file);
    /// <summary>대상의 시간각(°, 자오선에서 서쪽이 +)</summary>
    double HourAngleDeg(PrepContext ctx);
    double TargetAltitude(PrepContext ctx);
    /// <summary>방금 찍은 사진 주소 (실장비). 모의면 null — 화면이 그림으로 흉내</summary>
    string? PhotoUrl { get; }
}

public sealed record FrameShot(bool Ok, string? Problem, string? File, FrameStats? Stats);
/// <summary>자오선 반전 결과: 반전(필수) · 다시 가운데 · 가이딩 재개</summary>
public sealed record FlipOutcome(bool Flipped, bool Centered, bool Guiding);
public sealed record RefocusOutcome(bool Ok, int Position, double Hfr, string? Problem = null);
