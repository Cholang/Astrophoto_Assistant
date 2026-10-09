using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A. 오류를 아이라 말로 (2026-10-09 사용자 요청). N.I.N.A. 알림(토스트)은 영어·번역 누락("MISSING LABEL")이 섞여 있고,
/// Advanced API는 알림을 주지 않는다 → N.I.N.A. 로그 파일(%LOCALAPPDATA%\NINA\Logs, 같은 내용이 원인까지 남음)을 따라 읽어
/// 오류·경고를 진단 카드 형식(무슨 일 / 왜 / 이렇게, 원본은 접어서 — DESIGN.md 4장)으로 바꾼다.
/// </summary>
public sealed record NinaNotice(long Id, DateTimeOffset At, string Category, string Title, IReadOnlyList<string> Why, string Fix, string Raw, int Count);

public sealed class NinaNotices
{
    private readonly Lock _gate = new();
    private readonly List<NinaNotice> _items = [];
    private long _next = 1;

    /// <summary>after보다 뒤의 알림 (화면이 2초마다 묻는다)</summary>
    public IReadOnlyList<NinaNotice> Since(long after)
    {
        lock (_gate) return _items.Where(n => n.Id > after).ToList();
    }

    private readonly Dictionary<long, DateTimeOffset> _sentAt = []; // 화면에 새 번호로 전한 시각 (반복 알림을 30초에 한 번만 다시 전하려고)

    /// <summary>아이라가 마지막으로 알림을 만든 시각 — 이때만 N.I.N.A. 알림 창을 숨긴다 (Codex L09: 해석하지 못한 알림은 그대로 보이게)</summary>
    public DateTimeOffset LastAddedAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// 같은 알림이 1분 안에 다시 오면 새로 띄우지 않고 횟수만 (적도의 통신 오류는 속성마다 줄줄이 남는다).
    /// 반복이 이어지면 30초에 한 번 새 번호를 붙여 화면에 다시 전한다 — 화면은 after=번호로 묻기 때문에 번호가 그대로면 횟수 변화를 모른다 (Codex L06)
    /// </summary>
    public void Add(string category, string title, IReadOnlyList<string> why, string fix, string raw, DateTimeOffset at)
    {
        lock (_gate)
        {
            LastAddedAt = DateTimeOffset.Now;
            var i = _items.FindLastIndex(n => n.Title == title && at - n.At < TimeSpan.FromMinutes(1));
            if (i >= 0)
            {
                var old = _items[i];
                var bump = !_sentAt.TryGetValue(old.Id, out var sent) || at - sent >= TimeSpan.FromSeconds(30);
                var id = bump ? _next++ : old.Id;
                _items[i] = old with { Id = id, Count = old.Count + 1, At = at, Raw = raw };
                if (bump)
                {
                    _sentAt.Remove(old.Id);
                    _sentAt[id] = at;
                    // 번호 순서를 지키려고 맨 뒤로
                    var item = _items[i];
                    _items.RemoveAt(i);
                    _items.Add(item);
                }
                return;
            }
            var n = new NinaNotice(_next++, at, category, title, why, fix, raw, 1);
            _items.Add(n);
            _sentAt[n.Id] = at;
            if (_items.Count > 50)
            {
                _sentAt.Remove(_items[0].Id);
                _items.RemoveAt(0);
            }
        }
    }
}

/// <summary>N.I.N.A. 로그 한 건 → 알림 (모르는 오류는 일반 문장 + 원본, 소음은 버림)</summary>
public static class NinaLogRules
{
    public sealed record Entry(DateTimeOffset At, string Level, string Source, string Method, string Message, string Body);

    /// <summary>알림으로 바꿀 것이면 (분류, 제목, 왜, 이렇게). 버릴 것이면 null</summary>
    public static (string Category, string Title, string[] Why, string Fix)? Interpret(Entry e)
    {
        var all = e.Message + "\n" + e.Body;
        bool Has(string s) => all.Contains(s, StringComparison.OrdinalIgnoreCase);

        // 전원 허브 출력 번호가 없음 — 아이라는 N.I.N.A. 스위치 값을 바꾸지 않으므로 N.I.N.A.에서 실제로 바꾸려다 실패한 것 (Codex L04: 버리지 않는다)
        if (Has("No switch found for index"))
            return ("switch", "전원 허브의 출력 번호를 찾지 못했습니다",
                ["N.I.N.A.에서 없는 출력(포트) 번호를 바꾸려 했습니다", "허브 종류나 Empire 설정이 바뀌었습니다"],
                "N.I.N.A. 전원 허브(스위치) 화면에서 출력 목록을 확인해 주세요.");

        // N.I.N.A.를 막 켜 장비 목록을 만드는 중 장비 연결 — 아이라가 기다렸다 다시 연결한다. 다른 곳에서 난 같은 예외는 버리지 않는다 (Codex L04)
        if (e.Source == "Connection.cs" && e.Method == "DeviceConnect" && Has("Sequence contains no matching element")) return null;

        // USB 시리얼 포트가 없음 (케이블이 빠졌거나 다른 포트에 꽂아 번호가 바뀜) — 실기 로그에서 가장 흔한 연결 실패
        if (Regex.Match(all, @"'(COM\d+)' 포트가 없습니다|port '?(COM\d+)'? does not exist", RegexOptions.IgnoreCase) is { Success: true } port)
        {
            var com = port.Groups[1].Success ? port.Groups[1].Value : port.Groups[2].Value;
            return ("connect", $"장비의 USB 포트({com})를 찾지 못했습니다",
                ["USB 케이블이 빠졌습니다", "다른 USB 구멍에 꽂아 포트 번호가 바뀌었습니다", "장비 전원이 꺼져 있습니다"],
                $"케이블을 확인해 주세요. 다른 구멍에 꽂았다면 원래 구멍에 다시 꽂거나, 장비 드라이버 설정에서 {com} 대신 새 포트를 골라 주세요.");
        }

        // 포커서 멈춤 (자동초점 실패의 원인으로 함께 남는다 — 이쪽을 먼저)
        if (Has("Focuser stuck at position"))
            return ("focus", "포커서가 움직이지 않습니다",
                ["포커서가 끝에 닿아 멈춤 감지가 걸렸습니다", "포커서 전원이나 USB가 불안정합니다"],
                "포커서 설정 창(N.I.N.A. 포커서 톱니바퀴)에서 멈춤을 풀고(Clear stall) 다시 시도해 주세요.");

        // PHD2 프로그램과의 통신
        if (e.Source == "PHD2Guider.cs" && (Has("Unable to get response from phd2") || Has("SocketException") || Has("transport connection") || Has("Phd2 error while sending")))
            return ("guide", "PHD2와 연결이 끊겼습니다",
                ["PHD2가 꺼졌거나 멈췄습니다"], "PHD2가 켜져 있는지 확인해 주세요. 꺼졌으면 다시 켠 뒤 가이더를 다시 연결합니다.");
        if (Has("timed-out waiting for guider to settle"))
            return ("guide", "가이딩이 안정되지 않았습니다",
                ["바람이나 구름으로 별이 흔들립니다", "디더링 뒤 안정 기준이 너무 엄격합니다"], "하늘과 바람을 확인해 주세요. 계속되면 가이딩 그래프를 봐 주세요.");
        if (Has("PHD2 error:Image capture stopped"))
            return ("guide", "PHD2 가이드 카메라 촬영이 멈췄습니다",
                ["가이드 카메라 USB가 불안정합니다", "PHD2에서 촬영을 멈췄습니다"], "가이드 카메라 연결을 확인해 주세요.");

        if (Has("Camera Timeout"))
            return ("camera", "카메라가 사진을 내보내지 않았습니다",
                ["카메라 USB 연결이 불안정합니다", "카메라가 절전·자동 꺼짐으로 들어갔습니다"],
                "카메라 전원·USB와 절전 설정을 확인해 주세요.");

        // 장비 연결 실패
        if (e.Method == "Connect" && e.Source == "AscomDevice.cs" || e.Source == "CameraVM.cs" && e.Method == "ChooseCamera" || Has("Unable to connect to device"))
        {
            if (Has("WandererEmpire"))
                return ("connect", "전원 허브(WandererBox)에 연결하지 못했습니다",
                    ["Wanderer Empire가 허브와 통신하지 못했습니다", "허브 USB가 빠졌거나 다른 포트에 꽂혔습니다"],
                    "허브 USB 케이블을 확인하고, Wanderer Empire 창에서 허브가 연결되어 있는지 본 뒤 다시 연결해 주세요.");
            if (Has("Serial port is not connected") || Has("Telescope") || Has("OnStep"))
                return ("connect", "적도의에 연결하지 못했습니다",
                    ["적도의 전원이 꺼져 있습니다", "적도의 USB(시리얼) 케이블이 빠졌거나 포트가 바뀌었습니다"],
                    "적도의 전원과 USB 케이블을 확인한 뒤 다시 연결해 주세요.");
            if (Has("X-T5") || Has("Fujifilm") || Has("Camera"))
                return ("connect", "카메라에 연결하지 못했습니다",
                    ["카메라 전원이 꺼져 있습니다", "USB 케이블이 빠졌거나 카메라의 PC 연결 방식이 테더링이 아닙니다"],
                    "카메라 전원·USB와 카메라의 PC 연결 방식(테더링)을 확인한 뒤 다시 연결해 주세요.");
            if (Has("Focuser") || Has("Oasis"))
                return ("connect", "포커서에 연결하지 못했습니다",
                    ["포커서 USB가 빠졌거나 전원이 들어오지 않습니다", "다른 프로그램(포커서 설정 창 등)이 포커서를 쓰고 있습니다"],
                    "포커서 USB를 확인하고 포커서 설정 창이 열려 있으면 닫은 뒤 다시 연결해 주세요.");
            return ("connect", "N.I.N.A.가 장비에 연결하지 못했습니다",
                ["장비 전원이나 케이블 문제", "드라이버 설정 문제"], "장비 전원과 케이블을 확인한 뒤 다시 연결해 주세요.");
        }

        // 적도의 통신 (연결된 뒤 값 읽기 실패가 줄줄이)
        if (Has("GET of Telescope."))
            return ("mount", "적도의와 통신이 끊겼습니다",
                ["적도의 전원이 꺼졌습니다", "USB 케이블이 흔들려 빠졌습니다"],
                "적도의 전원과 USB 케이블을 확인해 주세요. 다시 연결되기 전에는 적도의를 움직이지 않습니다.");

        if (Has("Plate solve failed"))
            return ("solve", "사진에서 위치를 찾지 못했습니다 (플레이트 솔빙)",
                ["별이 너무 적게 찍혔습니다 (구름·초점·노출)", "망원경 초점거리·카메라 설정이 실제와 다릅니다"],
                "하늘이 맑은지, 초점이 맞는지 확인한 뒤 다시 시도해 주세요.");

        if (Has("suitable guide star") || Has("Failed to select guide star"))
            return ("guide", "PHD2가 가이드 별을 찾지 못했습니다",
                ["구름이 지나가거나 가이드 망원경 덮개가 닫혀 있습니다", "가이드 노출이 짧거나 초점이 흐립니다"],
                "가이드 망원경 덮개와 하늘을 확인해 주세요.");
        if (Has("equipment failed to connect"))
            return ("guide", "PHD2가 장비를 연결하지 못했습니다",
                ["가이드 카메라 USB가 빠졌거나 포트가 바뀌었습니다", "PHD2 장비 연결 창이 열려 있습니다"],
                "PHD2 장비 연결 창에서 카메라와 적도의를 다시 연결해 주세요.");
        if (Has("Start guiding has failed") || Has("Start guiding has timed out"))
            return ("guide", "가이딩을 시작하지 못했습니다",
                ["가이드 별이 없거나 구름이 지나갑니다", "캘리브레이션이 맞지 않습니다"],
                "하늘과 가이드 망원경을 확인해 주세요.");

        if (Has("AutoFocus did not complete"))
            return ("focus", "자동초점이 끝나지 못했습니다",
                ["별이 보이지 않거나 구름이 지나갔습니다", "포커서가 멈췄습니다"],
                "하늘과 포커서를 확인한 뒤 다시 자동초점을 해 주세요.");
        // 자동초점 지점마다 "별 없음" 경고는 소음 (끝난 결과로 알린다)
        if (Has("No stars detected in step") || Has("restoring the focuser position")) return null;

        if (Has("download") && Has("timeout") || Has("CAMERA-DOWNLOAD-TIMEOUT"))
            return ("camera", "카메라에서 사진을 내려받지 못했습니다",
                ["USB 연결이 불안정합니다", "카메라 화질 설정이 RAW가 아닙니다"],
                "카메라 USB와 화질 설정(RAW)을 확인해 주세요.");

        // 그 밖의 오류: 경고는 버리고, 오류만 일반 문장으로
        if (e.Level != "ERROR") return null;
        return ("other", "N.I.N.A.에서 오류가 났습니다", ["아래 원본 내용을 확인해 주세요"], "같은 일이 반복되면 N.I.N.A. 창에서 상태를 확인해 주세요.");
    }

    private static readonly Regex Head = new(@"^(\d{4}-\d{2}-\d{2}T[\d:.]+)\|(\w+)\|([^|]*)\|([^|]*)\|\d+\|?(.*)$", RegexOptions.Compiled);

    /// <summary>로그 줄들을 항목으로 (한 항목 = 머리 줄 + 다음 머리 줄 전까지의 줄 — 예외 내용)</summary>
    public static IEnumerable<Entry> Parse(IEnumerable<string> lines)
    {
        Entry? cur = null;
        var body = new StringBuilder();
        foreach (var line in lines)
        {
            var m = Head.Match(line);
            if (m.Success)
            {
                if (cur is not null) yield return cur with { Body = body.ToString() };
                body.Clear();
                cur = new Entry(DateTimeOffset.TryParse(m.Groups[1].Value, out var t) ? t : DateTimeOffset.Now,
                    m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value, "");
            }
            else if (cur is not null && body.Length < 2000) body.AppendLine(line);
        }
        if (cur is not null) yield return cur with { Body = body.ToString() };
    }
}

/// <summary>N.I.N.A. 로그를 1초마다 따라 읽는다. 켤 때는 지금 파일의 끝에서 시작(지난 오류를 다시 알리지 않음), 새 로그 파일(N.I.N.A.를 다시 켬)은 처음부터</summary>
public sealed class NinaLogWatcher(NinaNotices notices, ILogger<NinaLogWatcher> log) : BackgroundService
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? file = null;
        long pos = 0;
        var first = true;
        var pending = "";
        var idle = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var newest = Directory.Exists(Folder) ? new DirectoryInfo(Folder).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
                if (newest is not null)
                {
                    if (newest.FullName != file)
                    {
                        file = newest.FullName;
                        pos = first ? newest.Length : 0;
                        pending = "";
                    }
                    first = false;
                    if (newest.Length < pos) pos = 0;
                    // 마지막 항목을 기다렸는데 2초 동안 더 써지지 않으면 그대로 읽는다 (오류 뒤에 다른 로그가 없을 때)
                    if (newest.Length == pos && pending.Length > 0 && ++idle >= 2)
                    {
                        Report(pending.Split('\n'));
                        pending = "";
                    }
                    if (newest.Length > pos)
                    {
                        idle = 0;
                        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        s.Seek(pos, SeekOrigin.Begin);
                        using var r = new StreamReader(s, Encoding.UTF8);
                        var text = pending + await r.ReadToEndAsync(ct);
                        pos = s.Position;
                        // 마지막 항목은 예외 줄이 아직 덜 써졌을 수 있다 → 다음 머리 줄이 올 때까지 남겨 둔다
                        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                        var lastHead = lines.FindLastIndex(l => l.Length > 10 && char.IsDigit(l[0]) && l[4] == '-' && l.Contains('|'));
                        if (lastHead > 0)
                        {
                            pending = string.Join('\n', lines.Skip(lastHead));
                            lines = lines.Take(lastHead).ToList();
                        }
                        else { pending = text; lines = []; }
                        Report(lines);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogDebug(e, "N.I.N.A. 로그를 읽지 못함");
            }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void Report(IEnumerable<string> lines)
    {
        foreach (var e in NinaLogRules.Parse(lines.Select(l => l.TrimEnd('\r'))))
        {
            if (e.Level is not ("ERROR" or "WARNING")) continue;
            if (NinaLogRules.Interpret(e) is not { } n) continue;
            var raw = (e.Message + "\n" + e.Body).Trim();
            notices.Add(n.Category, n.Title, n.Why, n.Fix, raw.Length > 600 ? raw[..600] + "…" : raw, e.At);
        }
    }
}

/// <summary>
/// N.I.N.A. 알림(토스트) 창 숨기기 (2026-10-09 사용자 요청 — 같은 내용을 아이라가 진단 카드로 보여 준다).
/// 알림은 N.I.N.A. 프로세스의 "NotificationsWindow" 창 하나에 쌓인다(2026-10-09 확인) — 그 창만 숨기고 다른 대화창은 건드리지 않는다.
/// 아이라 창이 앞에 있을 때만 숨긴다: 사용자가 N.I.N.A.로 가 있으면 N.I.N.A. 알림을 그대로 본다.
/// 그리고 최근 10초 안에 아이라가 알림을 만들었을 때만 숨긴다 — 로그에 없거나 해석하지 못한 N.I.N.A. 알림은 사라지지 않고 그대로 보인다 (Codex L09·L03)
/// </summary>
public sealed class NinaToastHider(NinaNotices notices) : BackgroundService
{
    // 숨긴 알림 창과 숨긴 시각. 5초 안에 아이라 알림이 생기지 않으면 다시 보이고, 그 창이 스스로 닫힐 때까지 다시 숨기지 않는다
    private nint _hidden;
    private DateTimeOffset _hiddenAt;
    private nint _givenBack;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private void Tick()
    {
        // 숨긴 뒤 5초 안에 아이라 알림이 없으면(로그에 없거나 해석하지 못한 알림) 되돌려 보인다 — 알림이 아무 데도 안 보이는 일이 없게
        if (_hidden != 0 && DateTimeOffset.Now - _hiddenAt > TimeSpan.FromSeconds(5))
        {
            if (notices.LastAddedAt < _hiddenAt - TimeSpan.FromSeconds(3))
            {
                ShowWindowAsync(_hidden, SwShowNoActivate);
                _givenBack = _hidden;
            }
            _hidden = 0;
        }
        var toast = ToastWindow();
        if (toast == 0) { _givenBack = 0; return; } // 알림 창이 닫혔다 — 다음 알림부터 다시 숨긴다
        if (toast == _givenBack) return;
        if (toast == _hidden) { ShowWindowAsync(toast, SwHide); return; } // 숨긴 창에 새 알림이 쌓이며 다시 보임 — 기다리는 동안은 계속 숨김
        var own = Process.GetCurrentProcess().MainWindowHandle;
        if (own == 0 || GetForegroundWindow() != own) return; // 개발 서버(창 없음)·다른 창을 보는 중이면 그대로
        ShowWindowAsync(toast, SwHide);
        _hidden = toast;
        _hiddenAt = DateTimeOffset.Now;
    }

    /// <summary>보이는 N.I.N.A. 알림 창 (없으면 0)</summary>
    private static nint ToastWindow()
    {
        var pids = Process.GetProcessesByName("NINA").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        if (pids.Count == 0) return 0;
        nint found = 0;
        var title = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(h))
            {
                title.Clear();
                GetWindowText(h, title, title.Capacity);
                if (title.ToString() == "NotificationsWindow") { found = h; return false; }
            }
            return true;
        }, 0);
        return found;
    }

    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;
    private delegate bool EnumProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hWnd, int cmd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
