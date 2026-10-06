using System.Text.Json;
using Astro.Core.Sky;
using Astro.Server.Prepare.Tasks.Center;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ⑤ 센터링 실장비, 한 회차: 사진+플레이트 솔빙(N.I.N.A. capture?solve=true, 솔버는 N.I.N.A. 설정 — ASTAP·D50) → 대상과의 거리
/// → 1′보다 멀면 솔빙 위치로 동기화(sync) 후 대상으로 다시 이동. 하늘 전체 검색은 N.I.N.A. 솔버 설정(실패 시 blind)에 맡긴다.
/// 솔빙은 별이 필요해 맑은 날 확인.
/// </summary>
public sealed class RealCenterDevices(NinaRig rig, LiveImages live) : ICenterDevices
{
    private double _lastError = double.MaxValue;

    public async Task<CenterAttempt> AttemptAsync(double raDeg, double decDeg, double exposureSeconds, bool blind, CancellationToken ct)
    {
        var shot = await rig.CaptureAsync(exposureSeconds, solve: true, save: false, null, ct);
        if (!shot.Ok || shot.Result is not { } res) return new CenterAttempt(false, 0, null);
        if (res.TryGetProperty("Image", out var img) && img.ValueKind == JsonValueKind.String && img.GetBytesFromBase64() is { Length: > 0 } jpg) live.Set("photo", jpg, "image/jpeg"); // 중간 문자열 없이 바로 바이트로
        if (!res.TryGetProperty("PlateSolveResult", out var ps) || ps.ValueKind != JsonValueKind.Object || !NinaRig.Bool(ps, "Success"))
            return new CenterAttempt(false, 0, null);
        var c = ps.GetProperty("Coordinates");
        var solvedRa = NinaRig.Num(c, "RADegrees");
        var solvedDec = double.IsFinite(NinaRig.Num(c, "DECDegrees")) ? NinaRig.Num(c, "DECDegrees") : NinaRig.Num(c, "Dec");
        var angle = NinaRig.Num(ps, "PositionAngle");
        _lastError = Astronomy.Separation(new Equatorial(solvedRa, solvedDec), new Equatorial(raDeg, decDeg)) * 60;
        if (_lastError > CenterTask.TargetArcmin)
        {
            // 솔빙으로 맞춘 동기화는 정상 (DESIGN.md ⑤) → 그 뒤 대상으로 다시 이동
            await rig.SyncAsync(solvedRa, solvedDec, ct);
            if (await rig.StartSlewAsync(raDeg, decDeg, ct) is null)
                await rig.WaitSlewAsync(raDeg, decDeg, null, TimeSpan.FromMinutes(2), ct);
        }
        // 이 사진의 별 크기·별 수 (초점 확인이 장비 준비 때 초점과 비교). 못 읽으면 비교하지 않는다
        var stats = await rig.LastStatsAsync(ct);
        return new CenterAttempt(true, _lastError, double.IsFinite(angle) ? angle : null,
            stats is { Hfr: > 0 and var hfr } && double.IsFinite(hfr) ? hfr : null, stats?.Stars);
    }

    public async Task<bool> StopAsync(CancellationToken ct) =>
        await rig.AbortExposureAsync(ct) & await rig.StopSlewAndConfirmAsync(ct);

    public async Task<CenterEndState> ReadEndStateAsync(CancellationToken ct)
    {
        var m = await rig.MountAsync(ct);
        return new CenterEndState(_lastError, m is null || m.Slewing, m?.Tracking ?? false, await rig.CameraExposingAsync(ct));
    }
}
