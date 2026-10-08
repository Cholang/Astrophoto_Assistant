using System.Diagnostics;
using System.Text.Json;
using Astro.Core;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// SharpCap 극축 정렬 다리 (① 실장비). AA가 SharpCap을 가이드 카메라·스크립트와 함께 켜고(/camera, /runscript),
/// 스크립트는 0.5초마다 극축 정렬 상태를 AA로 보내고(POST /api/prepare/sharpcap/report) 답으로 받은 명령을 실행한다.
/// 2026-10-06 실기: 상태 단계 이름 첫 단계 = "First", 위치를 못 찾으면 조절량이 0,0으로 옴 → First·Second 단계의 값은 측정으로 쓰지 않는다.
/// 조절 단계의 실제 이름은 별이 있어야 나와 맑은 날 확인 (그 전에는 First·Second가 아니면 조절 단계로 본다).
/// </summary>
public sealed class SharpCapBridge(ILogger<SharpCapBridge> log)
{
    /// <summary>Background = 배경 밝기 0~1 (SharpCap 히스토그램 평균 ÷ 범위, 모르면 null), Gain = 지금 게인</summary>
    public sealed record Report(string Stage, bool Active, bool CanAdvance, double X, double Y, double ExposureMs, string Camera, DateTimeOffset At,
        double? Background = null, double? Gain = null)
    {
        /// <summary>조절량이 진짜 측정인가 (첫·둘째 사진 단계가 아님)</summary>
        public bool Adjusting => Active && Stage is not ("First" or "Second" or "" or "None");
    }

    private readonly Lock _gate = new();
    private readonly Queue<string> _commands = new();
    private Report? _last;
    private Process? _process;

    public Report? Last { get { lock (_gate) return _last; } }

    /// <summary>스크립트가 보낸 상태를 받고, 쌓인 명령을 돌려준다(; 로 구분)</summary>
    public string Receive(JsonElement body)
    {
        var r = new Report(
            Str(body, "stage"), body.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True,
            body.TryGetProperty("canAdvance", out var c) && c.ValueKind == JsonValueKind.True,
            Dbl(body, "x"), Dbl(body, "y"), Dbl(body, "exp"), Str(body, "camera"), DateTimeOffset.Now,
            body.TryGetProperty("bg", out var bg) && bg.TryGetDouble(out var b) && b >= 0 ? b : null,
            body.TryGetProperty("gain", out var gn) && gn.TryGetDouble(out var gv) ? gv : null);
        lock (_gate)
        {
            _last = r;
            var cmds = string.Join(';', _commands);
            _commands.Clear();
            return cmds;
        }
    }

    /// <summary>스크립트에 보낼 명령: advance · exposure:밀리초 · quit</summary>
    public void Send(string command)
    {
        lock (_gate)
        {
            _commands.Enqueue(command);
            if (command.StartsWith("exposure:", StringComparison.Ordinal) && double.TryParse(command[9..], System.Globalization.CultureInfo.InvariantCulture, out var ms))
                _commandedMs = (ms, DateTimeOffset.Now);
        }
    }

    private (double Ms, DateTimeOffset At)? _commandedMs;

    /// <summary>
    /// 사용자가 SharpCap에서 노출을 직접 바꿨는가 (2026-10-08 사용자 요청 — AA가 덮어쓰지 않게): AA가 보낸 값과 3초 넘게 다르면.
    /// 그 뒤로는 AA가 노출을 바꾸지 않는다
    /// </summary>
    public bool UserChangedExposure
    {
        get
        {
            lock (_gate)
                return _commandedMs is { } c && _last is { } r && r.At - c.At > TimeSpan.FromSeconds(3) && r.ExposureMs > 0
                    && Math.Abs(r.ExposureMs - c.Ms) > Math.Max(5, c.Ms * 0.05);
        }
    }

    /// <summary>스크립트가 1.5초마다 저장하는 "화면에 보이는 영상"의 이름 앞부분 (뒤에 -번호_WithDisplayStretch.png). 하늘 화면이 읽는다</summary>
    public static string ViewFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "sharpcap-view");

    /// <summary>저장된 영상 (5초 안에 쓴 것만). 없으면 null</summary>
    public static byte[]? LatestView()
    {
        var dir = Path.GetDirectoryName(ViewFile)!;
        try
        {
            var f = new DirectoryInfo(dir).EnumerateFiles("sharpcap-view*.png").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            if (f is null || DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromSeconds(5)) return null;
            using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool Running => _process is { HasExited: false } || Process.GetProcessesByName("SharpCap").Length > 0;

    /// <summary>SharpCap을 그 카메라와 AA 스크립트로 켜고, 스크립트의 첫 보고가 올 때까지 기다린다. 실패면 이유</summary>
    public async Task<string?> LaunchAsync(string cameraName, string reportUrl, CancellationToken ct)
    {
        var exe = FindExe();
        if (exe is null) return "SharpCap을 찾지 못했습니다. SharpCap 4.1을 설치해 주세요.";
        if (Running) return "SharpCap이 이미 켜져 있습니다. SharpCap을 닫은 뒤 다시 시도해 주세요.";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "sharpcap-polar.py");
        await File.WriteAllTextAsync(script, Script(reportUrl), ct);
        lock (_gate) { _last = null; _commands.Clear(); _commandedMs = null; }
        // 지난번 영상 지우기 (번호가 다시 0부터)
        foreach (var old in Directory.EnumerateFiles(dir, "sharpcap-view*"))
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // 켜지는 동안 AA 위로 뜨지 않게 AA 뒤로 (최소화하지 않는다: 최소화하면 창 캡처가 빈다)
        using var behind = Engine.BackgroundWindows.KeepBehind(TimeSpan.FromSeconds(10), "SharpCap");
        try
        {
            _process = Process.Start(new ProcessStartInfo(exe, $"/camera \"{cameraName}\" /runscript \"{script}\"") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            return $"SharpCap을 켜지 못했습니다 ({e.Message})";
        }
        var problem = await WaitStartedAsync(ct);
        // 실패면 AA가 켠 SharpCap을 닫아 둔다 — 남겨 두면 "다시 시도"가 "이미 켜져 있습니다"로 막힘 (2026-10-08 시뮬레이터 전체 시험에서 발견).
        // 켜기 전에 SharpCap이 없었던 것을 확인했으므로 지금 열린 SharpCap은 AA가 켠 것
        if (problem is not null && _process is { HasExited: false }) await CloseAsync(CancellationToken.None);
        return problem;
    }

    private async Task<string?> WaitStartedAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (Last is { } r)
                return r.Camera.Length > 0 ? null : "SharpCap이 가이드 카메라를 열지 못했습니다. 카메라 연결을 확인해 주세요.";
            if (_process is { HasExited: true }) return "SharpCap이 바로 꺼졌습니다";
            await Task.Delay(500, ct);
        }
        return "SharpCap에서 극축 정렬이 시작되지 않았습니다 (스크립트 응답 없음)";
    }

    /// <summary>조건이 맞는 보고가 올 때까지 (시간 초과면 null)</summary>
    public async Task<Report?> WaitAsync(Func<Report, bool> cond, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Last is { } r && cond(r)) return r;
            await Task.Delay(300, ct);
        }
        return null;
    }

    /// <summary>SharpCap 닫기 (스크립트에 끝 알림 → 창 닫기 → 안 되면 끝내기). 닫혔으면 true</summary>
    public async Task<bool> CloseAsync(CancellationToken ct)
    {
        Send("quit");
        await Task.Delay(800, ct);
        foreach (var p in Process.GetProcessesByName("SharpCap"))
        {
            using (p)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(10_000)) { log.LogWarning("SharpCap이 닫히지 않아 끝냅니다"); p.Kill(); p.WaitForExit(5_000); }
                }
                catch (InvalidOperationException) { }
            }
        }
        _process = null;
        return !Running;
    }

    private static string? FindExe()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var dir = Environment.GetFolderPath(root);
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.EnumerateDirectories(dir, "SharpCap*").OrderByDescending(x => x))
                if (File.Exists(Path.Combine(d, "SharpCap.exe"))) return Path.Combine(d, "SharpCap.exe");
        }
        return null;
    }

    private static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Dbl(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>SharpCap(IronPython) 스크립트: 카메라를 기다린 뒤 노출·게인을 맞추고 극축 정렬을 켜서 0.5초마다 보고, 답의 명령을 실행</summary>
    private static string Script(string url) => """
        # AA가 만든 파일 — 극축 정렬 상태를 AA로 보내고 AA의 명령을 받는다 (고치지 마세요)
        import clr, time, threading
        clr.AddReference("System.Net.WebClient")
        from System.Net import WebClient
        from System.IO import File

        URL = "__URL__"
        VIEW = r"__VIEW__"

        def post(body):
            try:
                c = WebClient()
                c.Headers.Add("Content-Type", "application/json")
                return c.UploadString(URL, body)
            except Exception as e:
                return ""

        def run():
            cam = None
            for i in range(60):
                cam = SharpCap.SelectedCamera
                if cam is not None: break
                time.sleep(1)
            if cam is None:
                post('{"stage":"","camera":""}')
                return
            try:
                cam.Controls.Exposure.ExposureMs = 1000
            except Exception: pass
            # 처음 게인: 범위가 아주 넓은 카메라(ToupTek 100~17만 등)는 로그 눈금 25%, 아니면 절반 (2026-10-08 실기: 80%면 136500으로 하얗게 포화)
            try:
                g = cam.Controls.Gain
                if g.Minimum > 0 and g.Maximum / g.Minimum > 100:
                    g.Value = round(g.Minimum * (g.Maximum / g.Minimum) ** 0.25)
                else:
                    g.Value = g.Minimum + (g.Maximum - g.Minimum) * 0.5
            except Exception: pass
            time.sleep(1)
            SharpCap.Transforms.SelectTransform("Polar Align")
            pa = SharpCap.PolarAlignment
            lastbg = -1
            for n in range(7200):
                # 배경 밝기 0~1: 히스토그램(첫 채널)의 평균 칸 ÷ 칸 수 (모르면 -1). 비트 수와 상관없게 칸에서 직접 계산
                bg = -1
                if n % 2 == 0:
                    try:
                        v = list(SharpCap.DisplayStretch.LastHistogram.Values)[0]
                        tot = float(sum(v))
                        if tot > 0: bg = sum([i * c for i, c in enumerate(v)]) / tot / len(v)
                    except Exception: pass
                    lastbg = bg
                else:
                    bg = lastbg
                gain = -1
                try: gain = cam.Controls.Gain.Value
                except Exception: pass
                # 하늘 화면용: 화면에 보이는 영상만 저장 (창 캡처는 SharpCap 창 전체가 나옴 — 2026-10-08 실기).
                # 같은 이름이면 SharpCap이 덮어쓸지 묻는 창을 띄우고 멈춘다 → 매번 새 이름, 두 번 전 파일은 지운다 (읽는 중인 최신 파일은 남김)
                if n % 3 == 0:
                    k = n / 3
                    try: cam.SaveAsViewed(VIEW + "-%d.png" % k)
                    except Exception: pass
                    for old in [VIEW + "-%d_WithDisplayStretch.png" % (k - 2), VIEW + "-%d_WithDisplayStretch.CameraSettings.txt" % (k - 2)]:
                        try: File.Delete(old)
                        except Exception: pass
                try:
                    o = pa.Offset
                    body = '{"stage":"%s","active":%s,"canAdvance":%s,"x":%.2f,"y":%.2f,"exp":%.1f,"camera":"%s","bg":%.4f,"gain":%.1f}' % (
                        pa.Stage, str(pa.IsActive).lower(), str(pa.CanAdvance).lower(), o.X, o.Y, cam.Controls.Exposure.ExposureMs, cam.DeviceName, bg, gain)
                except Exception as e:
                    body = '{"stage":"","camera":"%s"}' % cam.DeviceName
                reply = post(body) or ""
                for cmd in reply.split(";"):
                    if cmd == "advance":
                        try: pa.Advance()
                        except Exception: pass
                    elif cmd.startswith("exposure:"):
                        try: cam.Controls.Exposure.ExposureMs = float(cmd[9:])
                        except Exception: pass
                    elif cmd == "quit":
                        return
                time.sleep(0.5)

        threading.Thread(target=run).start()
        """.Replace("__URL__", url).Replace("__VIEW__", ViewFile);
}
