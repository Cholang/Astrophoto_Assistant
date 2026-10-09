using System.Globalization;
using System.Text.Json;
using Astro.Core.Sky;
using Astro.Nina;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// 준비 단계가 N.I.N.A. Advanced API로 하는 장비 명령 (2026-10-06 실내 실기로 응답 형식 확인).
/// - 이동: slew?waitForResult=false → info로 지켜봄. 멈춤: slew/stop → "이동 중 아님" + 좌표가 연속 두 번 같으면 멈춘 것(N.I.N.A. 정보는 약 2초마다 갱신)
/// - 홈: home → AtHome이 true가 될 때까지 (홈으로 가는 동안 Slewing은 false로 나옴)
/// - 추적: tracking?mode=0(항성)/4(멈춤) → 약 2초 뒤 반영
/// - 촬영: capture?duration= (시작) → capture?getResult= ("Capture already in progress"가 끝날 때까지). 실패해도 이전 사진을 줄 수 있음
/// - 가이딩: guider/start는 별이 없어도 2분쯤 뒤 "started"로 답함 → PHD2 상태로 확인해야 함
/// </summary>
public sealed class NinaRig(NinaApiClient nina)
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(3);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Pier = ASCOM SideOfPier ("pierEast" · "pierWest" · "pierUnknown")</summary>
    public sealed record MountState(bool Connected, bool Slewing, bool Tracking, bool AtHome, double RaDeg, double DecDeg, double SiderealHours, Site Site, string Pier = "pierUnknown");

    public async Task<MountState?> MountAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/mount/info", Short, ct);
        if (!r.Ok || r.Response is not { ValueKind: JsonValueKind.Object } m) return null;
        var c = m.GetProperty("Coordinates");
        return new MountState(Bool(m, "Connected"), Bool(m, "Slewing"), Bool(m, "TrackingEnabled"), Bool(m, "AtHome"),
            Num(c, "RADegrees"), Num(c, "Dec"), Num(m, "SiderealTime"), new Site(Num(m, "SiteLatitude"), Num(m, "SiteLongitude")),
            m.TryGetProperty("SideOfPier", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "pierUnknown" : "pierUnknown");
    }

    /// <summary>움직임이 멈출 때까지 (이동 중 아님 + 좌표가 연속 두 번 같음). timeout까지 안 멈추면 false</summary>
    public async Task<bool> WaitStillAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        (double, double)? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var m = await MountAsync(ct);
            if (m is null) return false;
            if (!m.Slewing && last is { } l && Math.Abs(l.Item1 - m.RaDeg) < 0.005 && Math.Abs(l.Item2 - m.DecDeg) < 0.005) return true;
            last = (m.RaDeg, m.DecDeg);
            await Task.Delay(1200, ct);
        }
        return false;
    }

    /// <summary>이동을 시작한다 (기다리지 않음). 실패면 이유</summary>
    public async Task<string?> StartSlewAsync(double raDeg, double decDeg, CancellationToken ct)
    {
        var r = await nina.RequestAsync($"equipment/mount/slew?ra={raDeg.ToString(Inv)}&dec={decDeg.ToString(Inv)}&waitForResult=false", Short, ct);
        return r.Ok ? null : r.Error ?? "적도의가 응답하지 않습니다";
    }

    /// <summary>이동이 끝날 때까지 남은 거리를 알린다. 끝나면 목표와의 거리(도), 시간 초과·끊김이면 null</summary>
    public async Task<double?> WaitSlewAsync(double raDeg, double decDeg, Action<double>? remaining, TimeSpan timeout, CancellationToken ct)
    {
        var target = new Equatorial(raDeg, decDeg);
        var deadline = DateTime.UtcNow + timeout;
        var stillSince = 0;
        (double Ra, double Dec)? last = null;
        await Task.Delay(1500, ct); // 시작 직후에는 Slewing이 아직 false일 수 있다
        while (DateTime.UtcNow < deadline)
        {
            var m = await MountAsync(ct);
            if (m is null) return null;
            var d = Astronomy.Separation(new Equatorial(m.RaDeg, m.DecDeg), target);
            remaining?.Invoke(d);
            stillSince = !m.Slewing && last is { } l && Math.Abs(l.Ra - m.RaDeg) < 0.01 && Math.Abs(l.Dec - m.DecDeg) < 0.01 ? stillSince + 1 : 0;
            if (stillSince >= 1) return d;
            last = (m.RaDeg, m.DecDeg);
            await Task.Delay(1000, ct);
        }
        return null;
    }

    /// <summary>이동 멈춤 + 멈춘 것 확인 (이동 중 아님 + 좌표가 연속 두 번 같음)</summary>
    public async Task<bool> StopSlewAndConfirmAsync(CancellationToken ct)
    {
        await nina.RequestAsync("equipment/mount/slew/stop", Short, ct);
        return await ConfirmStillAsync(ct);
    }

    public async Task<bool> ConfirmStillAsync(CancellationToken ct)
    {
        (double, double)? last = null;
        for (var i = 0; i < 12; i++)
        {
            var m = await MountAsync(ct);
            if (m is null) return false;
            if (!m.Slewing && last is { } l && Math.Abs(l.Item1 - m.RaDeg) < 0.005 && Math.Abs(l.Item2 - m.DecDeg) < 0.005) return true;
            last = (m.RaDeg, m.DecDeg);
            await Task.Delay(1200, ct);
        }
        return false;
    }

    /// <summary>추적 켜기(항성)/끄기. 반영됐는지 확인</summary>
    public async Task<bool> SetTrackingAsync(bool sidereal, CancellationToken ct)
    {
        var r = await nina.RequestAsync($"equipment/mount/tracking?mode={(sidereal ? 0 : 4)}", Short, ct);
        if (!r.Ok) return false;
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(1000, ct);
            if (await MountAsync(ct) is { } m && m.Tracking == sidereal) return true;
        }
        return false;
    }

    // ── 촬영 ─────────

    /// <summary>Started = 카메라가 촬영을 받아 시작함 (Ok가 아니어도 내려받기에서 실패한 것일 수 있음)</summary>
    public sealed record Shot(bool Ok, bool Started, string? Problem, JsonElement? Result, DateTimeOffset At);

    /// <summary>
    /// 한 장 찍고 결과를 받는다. solve = 플레이트 솔빙, save = 디스크에 저장(image-history에 남음).
    /// "Capture started"를 받은 뒤 결과가 올 때까지 기다린다(X-T5 1초 노출 ≈ 25초).
    /// </summary>
    /// imageType = LIGHT · FLAT · DARK · DARKFLAT · SNAPSHOT (없으면 저장할 때 LIGHT, 아니면 SNAPSHOT)
    public async Task<Shot> CaptureAsync(double seconds, bool solve, bool save, Action<int>? remaining, CancellationToken ct, int? gain = null, string? imageType = null)
    {
        var started = DateTimeOffset.Now;
        var type = imageType ?? (save ? "LIGHT" : "SNAPSHOT");
        var q = $"equipment/camera/capture?duration={seconds.ToString(Inv)}&solve={(solve ? "true" : "false")}&save={(save ? "true" : "false")}&imageType={type}" + (gain is { } g ? $"&gain={g}" : "");
        var start = await nina.RequestAsync(q, Short, ct);
        if (!start.Ok || start.Text != "Capture started")
            return new Shot(false, false, start.Error ?? start.Text ?? "카메라가 응답하지 않습니다", null, started);
        // 노출이 끝날 때까지 남은 초를 알린다
        for (var left = (int)Math.Ceiling(seconds); left > 0; left--)
        {
            remaining?.Invoke(left);
            await Task.Delay(1000, ct);
        }
        remaining?.Invoke(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds) + TimeSpan.FromSeconds(150);
        while (DateTime.UtcNow < deadline)
        {
            var r = await nina.RequestAsync("equipment/camera/capture?getResult=true&resize=true&scale=0.25&quality=80", Long, ct);
            if (r.Text == "Capture already in progress") { await Task.Delay(1500, ct); continue; }
            if (!r.Ok || r.Response is not { ValueKind: JsonValueKind.Object } res) return new Shot(false, true, r.Error ?? "사진을 받지 못했습니다", null, started);
            // 실패해도 이전 사진을 돌려줄 수 있다 (2026-10-06 실기) → 이번 촬영이 끝났다는 이벤트가 있어야 새 사진
            var events = await EventsSinceAsync(started, ct);
            if (events.Contains("CAMERA-DOWNLOAD-TIMEOUT")) return new Shot(false, true, "카메라에서 사진을 받지 못했습니다 (시간 초과)", null, started);
            if (!events.Contains("API-CAPTURE-FINISHED")) return new Shot(false, true, "이번 사진이 아닌 이전 사진이 돌아왔습니다", null, started);
            return new Shot(true, true, null, res, started);
        }
        return new Shot(false, true, "사진을 받는 데 너무 오래 걸립니다", null, started);
    }

    public sealed record Stats(int Stars, double Hfr, double Median, double Max, double Mean);

    public async Task<Stats?> LastStatsAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/camera/capture/statistics", Short, ct);
        return r.Ok && r.Response is { ValueKind: JsonValueKind.Object } s
            ? new Stats((int)Num(s, "Stars"), Num(s, "HFR"), Num(s, "Median"), Num(s, "Max"), Num(s, "Mean"))
            : null;
    }

    /// <summary>
    /// 가장 최근에 저장된 사진 기록 (save=true로 찍은 것). 없으면 null.
    /// File은 디스크의 전체 경로 — image-history의 Filename은 이름만이라(2026-10-06 시뮬레이터 확인) N.I.N.A. 이미지 폴더에서 찾는다.
    /// 확실하지 않으면(못 찾음·여럿) 빈 문자열 — 이름만 돌려주지 않는다(옮기면 안 되는 파일을 옮기지 않게, CX-NIGHT-05)
    /// </summary>
    public async Task<(DateTimeOffset Date, string File, double Hfr, int Stars)?> LastSavedAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("image-history?all=true", Short, ct);
        if (!r.Ok || r.Response is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() == 0) return null;
        var e = list[list.GetArrayLength() - 1];
        var date = e.TryGetProperty("Date", out var d) && DateTimeOffset.TryParse(d.GetString(), Inv, DateTimeStyles.AssumeLocal, out var dt) ? dt : DateTimeOffset.MinValue;
        var name = e.TryGetProperty("Filename", out var f) ? f.GetString() ?? "" : "";
        return (date, name.Length > 0 ? await FindSavedFileAsync(name, date, ct) ?? "" : "", Num(e, "HFR"), (int)Num(e, "Stars"));
    }

    /// <summary>N.I.N.A. 활성 프로필 전체. 모르면 null</summary>
    public async Task<JsonElement?> ProfileAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("profile/show?active=true", Short, ct);
        return r.Ok && r.Response is { ValueKind: JsonValueKind.Object } p ? p : null;
    }

    /// <summary>N.I.N.A. 프로필의 이미지 폴더(ImageFileSettings.FilePath). 모르면 null</summary>
    public async Task<string?> ImageFolderAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("profile/show?active=true", Short, ct);
        return r.Ok && r.Response is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("ImageFileSettings", out var s)
            && s.TryGetProperty("FilePath", out var fp) && fp.GetString() is { Length: > 0 } dir ? dir : null;
    }

    /// <summary>
    /// 저장된 사진의 전체 경로: 이미지 폴더 아래 전체에서 이름으로 찾고, 저장 시각(savedAt) 앞뒤 2분 안에 쓰인 파일이 **하나뿐일 때만** 돌려준다.
    /// 폴더 구조(날짜·대상·종류 폴더 등)에 기대지 않는다. 못 찾거나 여럿이면 null (CX-NIGHT-05)
    /// </summary>
    public async Task<string?> FindSavedFileAsync(string name, DateTimeOffset savedAt, CancellationToken ct)
    {
        if (await ImageFolderAsync(ct) is not { } root || !Directory.Exists(root)) return null;
        return FindSavedFile(root, name, savedAt);
    }

    internal static string? FindSavedFile(string root, string name, DateTimeOffset savedAt)
    {
        try
        {
            var window = TimeSpan.FromMinutes(2);
            var hits = new DirectoryInfo(root).EnumerateFiles(name, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(x => (new DateTimeOffset(x.LastWriteTimeUtc) - savedAt).Duration() <= window)
                .Take(2).ToList();
            return hits.Count == 1 ? hits[0].FullName : null;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<bool> CameraExposingAsync(CancellationToken ct) =>
        (await nina.RequestAsync("equipment/camera/info", Short, ct)).Response is { ValueKind: JsonValueKind.Object } c && Bool(c, "IsExposing");

    public async Task<bool> AbortExposureAsync(CancellationToken ct)
    {
        await nina.RequestAsync("equipment/camera/abort-exposure", Short, ct);
        for (var i = 0; i < 5; i++)
        {
            if (!await CameraExposingAsync(ct)) return true;
            await Task.Delay(1000, ct);
        }
        return false;
    }

    /// <summary>솔빙으로 얻은 지금 위치로 적도의를 동기화 (DESIGN.md ⑤ — 솔빙 동기화는 정상)</summary>
    public async Task<bool> SyncAsync(double raDeg, double decDeg, CancellationToken ct) =>
        (await nina.RequestAsync($"equipment/mount/sync?ra={raDeg.ToString(Inv)}&dec={decDeg.ToString(Inv)}", Short, ct)).Ok;

    // ── 포커서 ─────────

    public sealed record FocuserState(bool Connected, int Position, bool Moving, double? Temperature);

    public async Task<FocuserState?> FocuserAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/focuser/info", Short, ct);
        if (!r.Ok || r.Response is not { ValueKind: JsonValueKind.Object } f) return null;
        var t = Num(f, "Temperature");
        return new FocuserState(Bool(f, "Connected"), (int)Num(f, "Position"), Bool(f, "IsMoving"), double.IsFinite(t) ? t : null);
    }

    /// <summary>포커서 이름·드라이버 Id (Oasis인지 볼 때). 모르면 null</summary>
    public async Task<string?> FocuserNameAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/focuser/info", Short, ct);
        if (!r.Ok || r.Response is not { ValueKind: JsonValueKind.Object } f) return null;
        return string.Join(" ", new[] { "Name", "DisplayName", "DeviceId" }.Select(k => f.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null).Where(x => x is not null));
    }

    public async Task<string?> MoveFocuserAsync(int position, CancellationToken ct)
    {
        var r = await nina.RequestAsync($"equipment/focuser/move?position={position}", Short, ct);
        if (!r.Ok) return r.Error ?? "포커서가 응답하지 않습니다";
        // N.I.N.A.는 백래시 보정으로 목표를 지나쳤다가 돌아온다(2026-10-06 실기: +300 이동 중 +900에서 잠깐 섬)
        // → 목표에 닿을 때까지 기다리고, 다른 곳에서 5초 넘게 서 있을 때만 멈춘 것으로 본다
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        (int Pos, DateTime Since)? still = null;
        await Task.Delay(500, ct);
        while (DateTime.UtcNow < deadline)
        {
            if (await FocuserAsync(ct) is not { } f) return "포커서가 응답하지 않습니다";
            if (!f.Moving && f.Position == position) return null;
            if (f.Moving || still?.Pos != f.Position) still = f.Moving ? null : (f.Position, DateTime.UtcNow);
            else if (DateTime.UtcNow - still.Value.Since > TimeSpan.FromSeconds(5)) return $"포커서가 {f.Position}에서 멈췄습니다";
            await Task.Delay(500, ct);
        }
        return "포커서가 제시간에 도착하지 않았습니다";
    }

    public async Task StopFocuserAsync(CancellationToken ct) => await nina.RequestAsync("equipment/focuser/stop-move", Short, ct);

    public async Task<bool> StartAutofocusAsync(CancellationToken ct) => (await nina.RequestAsync("equipment/focuser/auto-focus", Short, ct)).Ok;
    public async Task CancelAutofocusAsync(CancellationToken ct) => await nina.RequestAsync("equipment/focuser/auto-focus?cancel=true", Short, ct);

    /// <summary>마지막 자동초점 기록 (Timestamp, CalculatedFocusPoint, MeasurePoints, RSquares …)</summary>
    public async Task<JsonElement?> LastAutofocusAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/focuser/last-af", Short, ct);
        return r.Ok && r.Response is { ValueKind: JsonValueKind.Object } a ? a : null;
    }

    // ── 촬영·마무리 (2026-10-07 — 실기 미확인: 장비가 돌아오면 확인) ─────────

    /// <summary>적도의 홈으로 (Go Home — Set Home은 절대 쓰지 않는다). AtHome이 될 때까지, 시간 초과면 false</summary>
    public async Task<bool> HomeAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/mount/home", Short, ct);
        if (!r.Ok) return false;
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000, ct);
            if (await MountAsync(ct) is { AtHome: true }) return true;
        }
        return false;
    }

    /// <summary>
    /// 자오선 반전 (N.I.N.A. mount/flip). 끝나고 멈출 때까지.
    /// 2026-10-06 시뮬레이터 확인: flip은 곧바로 "Flipping"으로 답하고 적도의는 2~5초 뒤에 움직이기 시작(약 14초에 끝, pierWest → pierEast).
    /// 반전이 필요 없는 쪽이면 같은 답만 하고 아무 일도 없다 → 적도의 방향(SideOfPier)이 바뀐 것으로 확인한다.
    /// 자오선 서쪽을 보는 정상 자세는 pierEast(ASCOM 규약 — 시뮬레이터도 자오선을 지난 대상으로 가면 pierEast)라 이미 그쪽이면 반전할 것이 없음.
    /// 방향을 모르는 적도의(pierUnknown)는 움직이기 시작한 것(Slewing)을 본 뒤 멈춤으로 확인
    /// </summary>
    /// <summary>반전 결과: Flipped = 반전 확인(정상 자세 + 멈춤), Still = 적도의가 멈춘 것을 확인 (실패여도 Still이면 다음 작업을 해도 됨)</summary>
    public sealed record FlipResult(bool Flipped, bool Still);

    public async Task<FlipResult> FlipAsync(CancellationToken ct)
    {
        if (await MountAsync(ct) is not { } before) return new FlipResult(false, false);
        // 이미 반전된 쪽: 그래도 멈춰 있는지 확인한 뒤 성공 (CX-NIGHT-01)
        if (before.Pier == "pierEast") { var s = await WaitStillAsync(TimeSpan.FromMinutes(1), ct); return s ? new FlipResult(true, true) : await StopAfterFailAsync(ct); }
        var r = await nina.RequestAsync("equipment/mount/flip", Long, ct);
        if (!r.Ok) return await StopAfterFailAsync(ct);
        // 방향이 pierEast로 바뀌어야 반전(pierUnknown은 아직 모름 — 다시 조회). 60초 안에 바뀌지 않아도 움직이는 중이면
        // 끝날 때까지 기다린다 (방향을 반전이 끝나야 바꾸는 장비 — CX-NIGHT-02). 방향을 아예 모르는 적도의는 움직였다가 멈춤으로
        var start = DateTime.UtcNow;
        var slewed = false;
        while (true)
        {
            await Task.Delay(1000, ct);
            if (await MountAsync(ct) is not { } m) return await StopAfterFailAsync(ct);
            slewed |= m.Slewing;
            if (m.Pier == "pierEast" || before.Pier == "pierUnknown" && slewed && !m.Slewing) break;
            var waited = DateTime.UtcNow - start;
            if (waited > TimeSpan.FromSeconds(60) && !m.Slewing || waited > TimeSpan.FromMinutes(4)) return await StopAfterFailAsync(ct);
        }
        return await WaitStillAsync(TimeSpan.FromMinutes(4), ct) ? new FlipResult(true, true) : await StopAfterFailAsync(ct);
    }

    /// <summary>반전 실패: 이동을 멈추고 멈춘 것을 확인 (CX-NIGHT-02 — 실패를 정지로 보지 않는다)</summary>
    private async Task<FlipResult> StopAfterFailAsync(CancellationToken ct) => new(false, await StopSlewAndConfirmAsync(ct));

    /// <summary>장비 연결 (가이더를 다시 잡을 때 등)</summary>
    public async Task<bool> ConnectAsync(string device, CancellationToken ct) =>
        (await nina.RequestAsync($"equipment/{device}/connect", Long, ct)).Ok;

    /// <summary>장비 연결 끊기 (camera · mount · focuser · guider · filterwheel · switch · flatdevice · rotator)</summary>
    public async Task<bool> DisconnectAsync(string device, CancellationToken ct) =>
        (await nina.RequestAsync($"equipment/{device}/disconnect", Short, ct)).Ok;

    /// <summary>카메라 비트 수 (플랫 밝기 % 계산). 모르면 null</summary>
    public async Task<int?> CameraBitDepthAsync(CancellationToken ct)
    {
        if ((await nina.RequestAsync("equipment/camera/info", Short, ct)).Response is not { ValueKind: JsonValueKind.Object } c) return null;
        var b = Num(c, "BitDepth");
        return double.IsFinite(b) && b > 0 ? (int)b : null;
    }

    // ── 가이딩 (N.I.N.A. 명령 — 시퀀스의 디더링·반전이 상태를 알도록, DESIGN.md ⑥) ─────────

    /// <summary>가이딩 시작. 별이 없어도 2분쯤 뒤 성공으로 답하므로 결과는 PHD2 상태로 확인할 것</summary>
    public async Task<NinaReply> StartGuidingAsync(bool calibrate, CancellationToken ct) =>
        await nina.RequestAsync($"equipment/guider/start?calibrate={(calibrate ? "true" : "false")}", Long, ct);

    public async Task<bool> StopGuidingAsync(CancellationToken ct) => (await nina.RequestAsync("equipment/guider/stop", TimeSpan.FromSeconds(60), ct)).Ok;

    /// <summary>최근 가이딩 오차 RMS(″) — N.I.N.A. 가이드 그래프 (값은 픽셀 × 화면 배율)</summary>
    public async Task<double?> GuideRmsArcsecAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/guider/graph", Short, ct);
        if (r.Response is not { ValueKind: JsonValueKind.Object } g || !g.TryGetProperty("RMS", out var rms)) return null;
        var total = Num(rms, "Total");
        var scale = Num(g, "PixelScale");
        return double.IsFinite(total) && double.IsFinite(scale) && Num(rms, "DataPoints") > 0 ? total * scale : null;
    }

    /// <summary>
    /// 이슬 열선 (WandererBox DC3 PWM — N.I.N.A. 스위치). Power = 0~1, Auto = WandererEmpire 자동 제어 중
    /// (2026-10-08 실기: 자동이면 읽기 전용 스위치 "PWM DC3: DC3", 설명 "Automatic control in progress…", 값 0~255 — 기준 15→5°C로 바꾸자 255→0).
    /// 스위치가 연결되지 않았거나 PWM 열선이 없으면 null
    /// </summary>
    public async Task<(double Power, bool Auto)?> DewHeaterAsync(CancellationToken ct)
    {
        var r = await nina.RequestAsync("equipment/switch/info", Short, ct);
        if (r.Response is not { ValueKind: JsonValueKind.Object } s || !Bool(s, "Connected")) return null;
        foreach (var group in new[] { "ReadonlySwitches", "WritableSwitches" })
        {
            if (!s.TryGetProperty(group, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var w in list.EnumerateArray())
            {
                var name = w.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                if (!name.Contains("PWM", StringComparison.OrdinalIgnoreCase)) continue;
                var desc = w.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                var max = Num(w, "Maximum");
                var value = Num(w, "Value");
                return (Math.Clamp(value / (max > 1 ? max : 255), 0, 1), desc.Contains("Automatic control", StringComparison.OrdinalIgnoreCase));
            }
        }
        return null;
    }

    public async Task<bool> GuiderConnectedAsync(CancellationToken ct) =>
        (await nina.RequestAsync("equipment/guider/info", Short, ct)).Response is { ValueKind: JsonValueKind.Object } g && Bool(g, "Connected");

    /// <summary>since 뒤에 일어난 N.I.N.A. 이벤트 이름들 (API-CAPTURE-FINISHED · CAMERA-DOWNLOAD-TIMEOUT · AUTOFOCUS-FINISHED · ERROR-AF · MOUNT-HOMED …)</summary>
    public async Task<HashSet<string>> EventsSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        var r = await nina.RequestAsync("event-history", Short, ct);
        var names = new HashSet<string>();
        if (r.Response is not { ValueKind: JsonValueKind.Array } list) return names;
        foreach (var e in list.EnumerateArray())
            if (e.TryGetProperty("Time", out var t) && DateTimeOffset.TryParse(t.GetString(), Inv, DateTimeStyles.None, out var at) && at >= since
                && e.TryGetProperty("Event", out var n) && n.GetString() is { } name)
                names.Add(name);
        return names;
    }

    /// <summary>since 뒤의 자동초점 측정점 (AUTOFOCUS-POINT-ADDED — 위치·HFR, 잴 때마다 하나씩, 2026-10-09 확인). 시간 순</summary>
    public async Task<IReadOnlyList<(int Position, double Hfr)>> AutofocusPointsAsync(DateTimeOffset since, CancellationToken ct)
    {
        var r = await nina.RequestAsync("event-history", Short, ct);
        var points = new List<(DateTimeOffset At, int Position, double Hfr)>();
        if (r.Response is not { ValueKind: JsonValueKind.Array } list) return [];
        foreach (var e in list.EnumerateArray())
            if (e.TryGetProperty("Event", out var n) && n.GetString() == "AUTOFOCUS-POINT-ADDED"
                && e.TryGetProperty("Time", out var t) && DateTimeOffset.TryParse(t.GetString(), Inv, DateTimeStyles.None, out var at) && at >= since)
                points.Add((at, (int)Num(e, "Position"), Num(e, "HFR")));
        return points.OrderBy(p => p.At).Select(p => (p.Position, p.Hfr)).ToList();
    }

    /// <summary>since 뒤에 prefix로 시작하는 이벤트가 마지막으로 일어난 시각 (없으면 null) — 진행이 멈췄는지 볼 때</summary>
    public async Task<DateTimeOffset?> LastEventAtAsync(string prefix, DateTimeOffset since, CancellationToken ct)
    {
        var r = await nina.RequestAsync("event-history", Short, ct);
        if (r.Response is not { ValueKind: JsonValueKind.Array } list) return null;
        DateTimeOffset? last = null;
        foreach (var e in list.EnumerateArray())
            if (e.TryGetProperty("Time", out var t) && DateTimeOffset.TryParse(t.GetString(), Inv, DateTimeStyles.None, out var at) && at >= since
                && e.TryGetProperty("Event", out var n) && n.GetString() is { } name && name.StartsWith(prefix, StringComparison.Ordinal)
                && (last is null || at > last))
                last = at;
        return last;
    }

    internal static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>숫자 (N.I.N.A.는 값이 없으면 문자열 "NaN"을 준다 — Codex 실기 조회 2026-10-06)</summary>
    internal static double Num(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v)) return double.NaN;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, Inv, out var d) => d,
            _ => double.NaN,
        };
    }
}
