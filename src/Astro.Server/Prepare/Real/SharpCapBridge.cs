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
    public sealed record Report(string Stage, bool Active, bool CanAdvance, double X, double Y, double ExposureMs, string Camera, DateTimeOffset At)
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
            Dbl(body, "x"), Dbl(body, "y"), Dbl(body, "exp"), Str(body, "camera"), DateTimeOffset.Now);
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
        lock (_gate) _commands.Enqueue(command);
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
        lock (_gate) { _last = null; _commands.Clear(); }
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

        URL = "__URL__"

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
            try:
                g = cam.Controls.Gain
                g.Value = g.Minimum + (g.Maximum - g.Minimum) * 0.8
            except Exception: pass
            time.sleep(1)
            SharpCap.Transforms.SelectTransform("Polar Align")
            pa = SharpCap.PolarAlignment
            for n in range(7200):
                try:
                    o = pa.Offset
                    body = '{"stage":"%s","active":%s,"canAdvance":%s,"x":%.2f,"y":%.2f,"exp":%.1f,"camera":"%s"}' % (
                        pa.Stage, str(pa.IsActive).lower(), str(pa.CanAdvance).lower(), o.X, o.Y, cam.Controls.Exposure.ExposureMs, cam.DeviceName)
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
        """.Replace("__URL__", url);
}
