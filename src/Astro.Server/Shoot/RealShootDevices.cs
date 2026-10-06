using System.Text.Json;
using Astro.Core.Sky;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Real;
using Astro.Server.Prepare.Tasks.Center;
using Astro.Server.Prepare.Tasks.Focus;

namespace Astro.Server.Shoot;

/// <summary>
/// 촬영 실장비 (2026-10-07 — 실기 미확인, 장비가 돌아오면 확인할 것은 docs/SHOOT_IMPLEMENTATION.md).
/// AA가 한 장씩 찍는다: N.I.N.A. capture(save, LIGHT) → image-history·통계로 별 크기·별 수·배경. 디더링은 PHD2 dither(안정될 때까지),
/// 가이딩 상태는 PHD2 앱 상태, 자오선 반전은 N.I.N.A. mount/flip → 센터링(준비의 센터링 장비) → 가이딩 재시작, 초점은 준비의 초점 장비.
/// ISO는 N.I.N.A. gain으로 넘긴다 (X-T5는 gain이 ISO로 동작 — 사용자가 예전 촬영에서 확인).
/// </summary>
public sealed class RealShootDevices(NinaRig rig, Phd2Client phd2, LiveImages live, ICenterDevices center, IFocusDevices focus, ILogger<RealShootDevices> log) : IShootDevices
{
    public string? PhotoUrl => "/api/prepare/live/shoot";

    public async Task<FrameShot> ExposeAsync(int seconds, int iso, Action<int> elapsed, CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var shot = await rig.CaptureAsync(seconds, solve: false, save: true, left => elapsed(seconds - left), ct, gain: iso, imageType: "LIGHT");
        if (!shot.Ok || shot.Result is not { } res) return new FrameShot(false, shot.Problem, null, null);
        if (res.TryGetProperty("Image", out var img) && img.GetString() is { Length: > 0 } b64) live.Set("shoot", Convert.FromBase64String(b64), "image/jpeg");
        // 이번 촬영 뒤에 저장된 기록인지 확인 (CX-SHOOT-04) — 저장 기록이 늦게 갱신될 수 있어 잠깐 다시 본다.
        // 확인 못 하면 실패: 이전 사진의 통계로 평가하거나 이전 파일을 옮기지 않는다
        (DateTimeOffset Date, string File, double Hfr, int Stars)? saved = null;
        for (var i = 0; i < 10; i++)
        {
            if (await rig.LastSavedAsync(ct) is { } s && s.Date >= started && s.File.Length > 0) { saved = s; break; }
            await Task.Delay(1000, ct);
        }
        if (saved is null) return new FrameShot(false, "저장된 사진을 확인하지 못했습니다", null, null);
        var stats = await rig.LastStatsAsync(ct);
        var hfr = saved is { Hfr: > 0 and var h } && double.IsFinite(h) ? h : stats?.Hfr ?? 0;
        var stars = saved?.Stars ?? stats?.Stars ?? 0;
        double? mean = stats is { Mean: var m } && double.IsFinite(m) ? m : null;
        return new FrameShot(true, null, saved?.File, new FrameStats(double.IsFinite(hfr) ? hfr : 0, stars, mean, null, null));
    }

    public Task<double?> GuideRmsAsync(CancellationToken ct) => rig.GuideRmsArcsecAsync(ct);

    // ── 가이드 별 지켜보기: PHD2 이벤트(GuideStep·StarLost)를 계속 받아 최근 SNR·HFD·튐을 모은다 ─────────

    private readonly Lock _gate = new();
    private readonly List<double> _snr = [], _hfd = [], _jump = [];
    private Task? _listen;
    private int _guideMs;
    private long _steps;

    public int GuideExposureMs => _guideMs;

    private CancellationTokenSource? _listenCts;

    private void EnsureListening(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_listen is { IsCompleted: false }) return;
            _listenCts = new CancellationTokenSource();
            var token = _listenCts.Token;
            _listen = Task.Run(() => ListenAsync(token), CancellationToken.None);
        }
    }

    /// <summary>촬영이 끝나면 이벤트 수신을 멈춘다 (PHD2 연결 자체는 다른 곳도 쓰므로 끊지 않음)</summary>
    public void EndSession()
    {
        lock (_gate)
        {
            _listenCts?.Cancel();
            _listenCts?.Dispose();
            _listenCts = null;
            _listen = null;
        }
    }

    public async Task<bool> MountTrackingAsync(CancellationToken ct) => (await rig.MountAsync(ct))?.Tracking ?? false;

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ev = await phd2.SubscribeAsync(ct);
                if (_guideMs == 0) _guideMs = (await phd2.CallAsync("get_exposure", ct: ct)).GetInt32();
                while (await ev.Reader.WaitToReadAsync(ct))
                    while (ev.Reader.TryRead(out var e))
                    {
                        var name = Phd2Events.Name(e);
                        if (name != "GuideStep" && name != "StarLost") continue;
                        lock (_gate)
                        {
                            // 별을 잃은 프레임은 SNR·HFD가 0 → 평균에 넣지 않고 오류 종류만 센다 (2026-10-06 시뮬레이터 확인)
                            if (name == "StarLost")
                            {
                                PushCode(e.TryGetProperty("ErrorCode", out var code) && code.TryGetInt32(out var c) && c != 0 ? c : -1);
                                continue;
                            }
                            PushCode(0);
                            _steps++;
                            if (e.TryGetProperty("SNR", out var snr) && snr.TryGetDouble(out var s)) Push(_snr, s);
                            if (e.TryGetProperty("HFD", out var hfd) && hfd.TryGetDouble(out var h)) Push(_hfd, h);
                            var dx = e.TryGetProperty("dx", out var x) && x.TryGetDouble(out var xv) ? Math.Abs(xv) : 0;
                            var dy = e.TryGetProperty("dy", out var y) && y.TryGetDouble(out var yv) ? Math.Abs(yv) : 0;
                            Push(_jump, Math.Max(dx, dy));
                        }
                    }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) when (e is Phd2Exception or IOException or InvalidOperationException)
            {
                log.LogDebug("PHD2 이벤트 다시 연결: {Message}", e.Message);
            }
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static void Push(List<double> xs, double v)
    {
        xs.Add(v);
        if (xs.Count > 60) xs.RemoveAt(0);
    }

    // 최근 프레임 결과: 0 = 별을 잡음, 그 밖 = StarLost의 ErrorCode (4 = HFD가 낮음, 모르면 −1)
    private readonly List<int> _codes = [];
    private const int CodeWindow = 30;
    public const int LowHfdCode = 4;

    private void PushCode(int code)
    {
        _codes.Add(code);
        if (_codes.Count > CodeWindow) _codes.RemoveAt(0);
    }

    public async Task<GuideRaw> GuideRawAsync(CancellationToken ct)
    {
        EnsureListening(ct);
        string state;
        try { state = await phd2.AppStateAsync(ct); }
        catch (Phd2Exception) { return new GuideRaw(false, false, (await rig.MountAsync(ct))?.Tracking ?? true, [], [], null, null, _steps); }
        var connected = state != "" && await rig.GuiderConnectedAsync(ct);
        var mount = await rig.MountAsync(ct);
        lock (_gate)
            return new GuideRaw(connected, state == "Guiding", mount?.Tracking ?? true, _snr.ToArray(), _hfd.ToArray(),
                _jump.Count > 0 ? _jump.TakeLast(3).Max() : null,
                null, // 이슬 여유: WandererBox 기온·습도 읽기는 아직 (docs/SHOOT_IMPLEMENTATION.md 확인 목록)
                _steps, _codes.Count(c => c != 0), _codes.Count(c => c == LowHfdCode));
    }

    public async Task<bool> SetGuideExposureAsync(int ms, CancellationToken ct)
    {
        try { await phd2.CallAsync("set_exposure", new object[] { ms }, ct); _guideMs = ms; return true; }
        catch (Phd2Exception) { return false; }
    }

    /// <summary>
    /// 별 다시 고르기. 가이딩 중(LostLock 포함)에는 find_star가 거절되므로(2026-10-07 PHD2 시뮬레이터 실기)
    /// 가이딩 멈춤 → 루프 → 별 고르기 → N.I.N.A.로 가이딩 다시 (보정값은 그대로)
    /// </summary>
    public async Task<bool> ReselectStarAsync(CancellationToken ct)
    {
        try
        {
            await phd2.CallAsync("stop_capture", ct: ct);
            for (var i = 0; i < 10 && await phd2.AppStateAsync(ct) != "Stopped"; i++) await Task.Delay(1000, ct);
            await phd2.CallAsync("loop", ct: ct);
            await Task.Delay(Math.Max(_guideMs, 1000) * 2 + 1500, ct);
            await phd2.CallAsync("find_star", ct: ct, timeout: TimeSpan.FromSeconds(30));
        }
        catch (Phd2Exception) { return false; }
        return await ResumeGuidingAsync(ct);
    }

    public async Task<bool> ReconnectGuiderAsync(CancellationToken ct)
    {
        await rig.DisconnectAsync("guider", ct);
        await Task.Delay(2000, ct);
        return await rig.ConnectAsync("guider", ct);
    }

    public async Task<bool> RecenterAsync(PrepContext ctx, CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var a = await center.AttemptAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees, 2, blind: false, ct);
            if (a.Solved && a.ErrorArcmin <= CenterTask.TargetArcmin) return true;
        }
        return false;
    }

    public Task<bool> AbortExposureAsync(CancellationToken ct) => rig.AbortExposureAsync(ct);

    /// <summary>열선 제어는 아직 (WandererBox 포트를 N.I.N.A. switch로 다룰 수 있는지 확인 뒤) — 알림만</summary>
    public Task<bool> DewHeaterBoostAsync(CancellationToken ct) => Task.FromResult(false);

    private async Task<bool> GuidingAsync(CancellationToken ct)
    {
        try { return await phd2.AppStateAsync(ct) == "Guiding"; }
        catch (Phd2Exception) { return false; }
    }

    public async Task<bool> ResumeGuidingAsync(CancellationToken ct)
    {
        _ = rig.StartGuidingAsync(calibrate: false, ct); // 별이 없어도 늦게 답하므로 기다리지 않고 PHD2 상태로 본다
        await Task.Delay(5000, ct);
        return await GuidingAsync(ct);
    }

    public async Task<bool> StopGuidingAsync(CancellationToken ct)
    {
        await rig.StopGuidingAsync(ct); // 답은 참고만 — 확인은 PHD2 상태로
        var direct = false;
        for (var i = 0; i < 15; i++)
        {
            try
            {
                // N.I.N.A. 가이딩 정지 뒤 PHD2는 Looping으로 남는다 (2026-10-06 실기). LostLock·Calibrating·조회 실패는 멈춤이 아님
                var st = await phd2.AppStateAsync(ct);
                if (st is "Stopped" or "Looping") return true;
                // N.I.N.A.가 꺼졌거나 명령이 안 먹으면 PHD2에 직접 (한 번)
                if (!direct && st is "Guiding" or "LostLock" or "Calibrating") { direct = true; await phd2.CallAsync("stop_capture", ct: ct); }
            }
            catch (Phd2Exception) { /* 조회 실패 — 다시 */ }
            await Task.Delay(1000, ct);
        }
        return false;
    }

    public async Task<bool> DitherAsync(CancellationToken ct)
    {
        try
        {
            using var ev = await phd2.SubscribeAsync(ct);
            await phd2.CallAsync("dither", new { amount = 5, raOnly = false, settle = new { pixels = 1.5, time = 10, timeout = 90 } }, ct);
            var done = await ev.WaitAsync(["SettleDone"], TimeSpan.FromSeconds(120), ct);
            if (done is { } d && (!d.TryGetProperty("Status", out var s) || s.GetInt32() == 0)) return true;
        }
        catch (Phd2Exception e)
        {
            log.LogWarning("디더링 실패: {Message}", e.Message);
            return false;
        }
        // 안정화 시간 초과: 별을 잃은 프레임 하나에도 PHD2의 안정 시간이 0으로 돌아간다(2026-10-06 시뮬레이터 확인) → 30초 더 직접 지켜본다
        return await WaitSettledAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), ct);
    }

    public const double SettlePixels = 1.5;

    public async Task<bool> WaitSettledAsync(TimeSpan hold, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var ev = await phd2.SubscribeAsync(ct);
            var deadline = DateTime.UtcNow + timeout;
            DateTime? since = null;
            while (DateTime.UtcNow < deadline)
            {
                if (await ev.WaitAsync(["GuideStep", "StarLost"], deadline - DateTime.UtcNow, ct) is not { } e) break;
                var ok = Phd2Events.Name(e) == "GuideStep"
                    && e.TryGetProperty("RADistanceRaw", out var ra) && e.TryGetProperty("DECDistanceRaw", out var de)
                    && Math.Sqrt(ra.GetDouble() * ra.GetDouble() + de.GetDouble() * de.GetDouble()) <= SettlePixels;
                if (!ok) { since = null; continue; }
                since ??= DateTime.UtcNow;
                if (DateTime.UtcNow - since >= hold) return true;
            }
        }
        catch (Phd2Exception e) { log.LogWarning("가이딩 안정 확인 실패: {Message}", e.Message); }
        return false;
    }

    public async Task<FlipOutcome> FlipAsync(PrepContext ctx, Action<int> step, CancellationToken ct)
    {
        step(0);
        if (ctx.HasGuider && !await StopGuidingAsync(ct)) return new FlipOutcome(false, false, false); // 가이딩을 멈추지 못하면 반전하지 않는다
        if (!await rig.FlipAsync(ct)) return new FlipOutcome(false, false, false);
        step(1);
        var centered = false;
        for (var i = 0; i < 5 && !centered; i++)
        {
            var a = await center.AttemptAsync(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees, 2, blind: false, ct);
            centered = a.Solved && a.ErrorArcmin <= CenterTask.TargetArcmin;
        }
        step(2);
        var guiding = !ctx.HasGuider || await ResumeGuidingAsync(ct);
        return new FlipOutcome(true, centered, guiding);
    }

    public async Task<RefocusOutcome> RefocusAsync(Action<int, double> point, CancellationToken ct)
    {
        var af = await focus.AutofocusAsync(point, ct);
        return new RefocusOutcome(af.Ok && af.StarsFound && af.CurveGood, af.BestPosition, af.Hfr, af.Problem);
    }

    public Task<double?> TemperatureAsync(CancellationToken ct) => focus.TemperatureAsync(ct);

    public string? MoveToExcluded(string file)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(file) ?? "", "제외");
            Directory.CreateDirectory(dir);
            var to = Path.Combine(dir, Path.GetFileName(file));
            File.Move(file, to, overwrite: false);
            return to;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(e, "제외 폴더로 옮기지 못함: {File}", file);
            return null;
        }
    }

    public double HourAngleDeg(PrepContext ctx) =>
        Astronomy.HourAngle(new Equatorial(ctx.Plan.RaDegrees, ctx.Plan.DecDegrees), ctx.Site, ctx.Now().UtcDateTime);

    public double TargetAltitude(PrepContext ctx) => ctx.TargetAltitude(ctx.Now());
}
