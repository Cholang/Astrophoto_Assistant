using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Astro.Server.Engine;

/// <summary>
/// AA가 다른 프로그램을 켜거나(N.I.N.A.) 그 프로그램이 장비 연결 중에 저절로 켜질 때(PHD2·Wanderer Empire),
/// 그 창이 AA 위로 올라와 Alt+Tab을 해야 하는 일이 없게 한다 (2026-10-06 사용자 요청).
/// 2026-10-08 사용자 결정: 새 창을 최소화하면 한 번 보였다 사라져(N.I.N.A. 로고·PHD2 최대화) 오히려 문제처럼 보임 →
/// 지켜보는 동안 AA 창을 잠깐 "항상 위"로 두어 새 창이 AA 뒤에서 열리게 하고, 끝나면 되돌린다. 최소화하지 않는다.
/// 새 창이 포커스를 가져가면 AA로 되돌린다. 시작 옵션의 "최소화"는 WPF 프로그램(N.I.N.A.)이 무시한다.
/// 지켜보는 때만: 그 뒤에 사용자가 직접 연 창은 건드리지 않는다. 지켜보는 프로그램 목록에 없는 창(ASCOM 드라이버 설정 창 등)도 건드리지 않는다.
/// 주의: SharpCap은 최소화하면 창 캡처(PrintWindow)가 비므로 여기 넣지 않는다 (준비 ① 실장비 어댑터에서 따로).
/// </summary>
public static class BackgroundWindows
{
    public const string Nina = "NINA";
    public const string Phd2 = "phd2";
    public const string WandererEmpire = "Wanderer Empire";

    /// <summary>장비 연결 중 저절로 켜지는 프로그램: 가이더 → PHD2, 전원 허브 → Wanderer Empire. 없으면 null</summary>
    public static IDisposable? ForConnect(string kind) => kind switch
    {
        "guider" => Watch(TimeSpan.FromSeconds(5), Phd2),
        "switch" => Watch(TimeSpan.FromSeconds(5), WandererEmpire),
        _ => null,
    };

    /// <summary>
    /// 최소화하지 않고 AA 뒤로만 보낸다 (SharpCap — 최소화하면 창 캡처가 빈다). 지켜보는 동안 새 창을 맨 아래로 보내고 AA 창을 앞으로.
    /// </summary>
    public static IDisposable KeepBehind(TimeSpan grace, params string[] processNames) => new Watcher(processNames, grace, minimize: false);

    /// <summary>지금부터 AA를 "항상 위"로 두고 이 프로그램들의 새 창이 포커스를 가져가면 AA로 되돌린다. Dispose 뒤에도 grace 동안 더 (조금 늦게 뜨는 창)</summary>
    public static IDisposable Watch(TimeSpan grace, params string[] processNames) => new Watcher(processNames, grace, minimize: false, onTop: true);

    // AA "항상 위"는 지켜보기가 여럿 겹칠 수 있어(N.I.N.A. 켜기 + 가이더 연결) 마지막 하나가 끝날 때 푼다
    private static readonly Lock TopGate = new();
    private static int _topCount;

    private static void HoldTop(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (TopGate)
        {
            _topCount += on ? 1 : -1;
            if (on ? _topCount != 1 : _topCount != 0) return;
            if (Process.GetCurrentProcess().MainWindowHandle is var own and not 0)
                SetWindowPos(own, on ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private sealed class Watcher : IDisposable
    {
        private static readonly TimeSpan MaxWatch = TimeSpan.FromMinutes(3); // 안전선: Dispose를 잊어도 끝난다
        private readonly CancellationTokenSource _stop = new();
        private readonly TimeSpan _grace;

        private readonly bool _onTop;
        private int _released;

        public Watcher(string[] names, TimeSpan grace, bool minimize, bool onTop = false)
        {
            _grace = grace;
            _onTop = onTop;
            if (!OperatingSystem.IsWindows()) return;
            if (onTop) HoldTop(true);
            // 지켜보기 전부터 있던 창(사용자가 열어 둔 것)은 건드리지 않는다
            var before = Windows(names).ToHashSet();
            _stop.CancelAfter(MaxWatch);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        // 지켜보는 동안 다시 펼쳐지는 창(프로그램이 로딩 끝에 창을 다시 띄움)도 다시 내린다
                        foreach (var h in Windows(names))
                        {
                            if (before.Contains(h)) continue;
                            if (minimize)
                            {
                                if (!IsIconic(h)) ShowWindowAsync(h, SwShowMinNoActive);
                            }
                            else if (_onTop)
                            {
                                // AA가 위에 있으니 창은 그대로 두고, 포커스만 AA로
                                if (GetForegroundWindow() == h && Process.GetCurrentProcess().MainWindowHandle is var own and not 0) SetForegroundWindow(own);
                            }
                            else if (GetForegroundWindow() == h)
                            {
                                SetWindowPos(h, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                                if (Process.GetCurrentProcess().MainWindowHandle is var own and not 0) SetForegroundWindow(own);
                            }
                        }
                        await Task.Delay(150, _stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                finally { Release(); }
            });
        }

        private void Release()
        {
            if (_onTop && Interlocked.Exchange(ref _released, 1) == 0) HoldTop(false);
        }

        public void Dispose() => _stop.CancelAfter(_grace);
    }

    /// <summary>그 프로세스들의 보이는 맨 위 창 (주인 창이 없는 것만 — 대화 상자는 주인 창과 함께 움직인다)</summary>
    private static IEnumerable<nint> Windows(string[] names)
    {
        var pids = new HashSet<uint>();
        foreach (var name in names)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                    pids.Add((uint)p.Id);
        if (pids.Count == 0) return [];
        var found = new List<nint>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(h) && GetWindow(h, GwOwner) == 0) found.Add(h);
            return true;
        }, 0);
        return found;
    }

    private const int SwShowMinNoActive = 7;
    private static readonly nint HwndBottom = 1, HwndTopmost = -1, HwndNoTopmost = -2;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
}
