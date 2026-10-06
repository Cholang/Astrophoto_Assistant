using System.Text.Json;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ⑦ 시험 사진 실장비: N.I.N.A.로 계획 노출 한 장(저장함 — image-history에 남음) → 통계(별 수·HFR·밝기)와 가이딩 오차.
/// 2026-10-06 실기(X-T5): 1초 노출에 결과까지 약 25초(RAW 84MB). 카메라 화질이 JPEG면 모든 값 0인 사진이 온다 → 받지 못한 것으로 본다.
/// 실패해도 이전 사진이 올 수 있어 NinaRig가 이벤트로 이번 사진인지 확인한다.
/// 별 길쭉함(이심률)·포화 비율은 N.I.N.A.가 주지 않아 0으로 둔다 (판정에서 빠짐 — 나중에 사진을 직접 분석).
/// </summary>
public sealed class RealTestShotDevices(NinaRig rig, LiveImages live) : ITestShotDevices
{
    private NinaRig.Shot? _shot;
    private DateTimeOffset _started = DateTimeOffset.MinValue;

    public async Task<bool> ExposeAsync(int exposureSeconds, int iso, Action<int> remaining, CancellationToken ct)
    {
        _started = DateTimeOffset.Now;
        _shot = await rig.CaptureAsync(exposureSeconds, solve: false, save: true, remaining, ct, gain: iso);
        // 카메라가 촬영을 받지 않았으면 노출 실패. 받았지만 내려받기에서 실패한 것은 DownloadAndAnalyzeAsync가 null로
        return _shot.Started;
    }

    public async Task<ShotStats?> DownloadAndAnalyzeAsync(DateTimeOffset since, CancellationToken ct)
    {
        if (_shot is not { Ok: true, Result: { } res } shot || shot.At < since) return null;
        if (await rig.LastStatsAsync(ct) is not { } s || s.Max <= 0) return null; // 전부 0 = JPEG 설정 등으로 사진 데이터가 빔
        if (res.TryGetProperty("Image", out var img) && img.ValueKind == JsonValueKind.String && img.GetBytesFromBase64() is { Length: > 0 } jpg) live.Set("test", jpg, "image/jpeg"); // 중간 문자열 없이 바로 바이트로
        var saved = await rig.LastSavedAsync(ct);
        var file = saved is { } f && f.Date >= since ? f.File : "";
        var rms = await rig.GuideRmsArcsecAsync(ct) ?? 0;
        return new ShotStats(s.Hfr, 0, s.Median / 65535.0, 0, rms, file);
    }

    public Task<bool> StopAsync(CancellationToken ct) => rig.AbortExposureAsync(ct);

    public async Task<TestShotEndState> ReadEndStateAsync(CancellationToken ct)
    {
        var saved = await rig.LastSavedAsync(ct);
        return new TestShotEndState(await rig.CameraExposingAsync(ct), await GuidingAsync(ct), saved is { } f && f.Date >= _started);
    }

    private async Task<bool> GuidingAsync(CancellationToken ct) => await rig.GuideRmsArcsecAsync(ct) is not null;
}
