using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// PHD2 창 확인 (사전 점검 F03): 장비 연결 창이 열려 있으면 PHD2가 가이드 카메라를 놓지 못한다 (2026-10-08 실기).
/// 이 창은 닫아도 숨겨진 채 남으므로(2026-10-09 확인 — 한국어 제목 "장비 연결") 보이는 창만 센다
/// </summary>
public static class Phd2Windows
{
    private static readonly string[] ConnectTitles = ["장비 연결", "Connect Equipment"];

    public static bool ConnectDialogOpen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var pids = Process.GetProcessesByName("phd2").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        if (pids.Count == 0) return false;
        var open = false;
        var title = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(h))
            {
                title.Clear();
                GetWindowText(h, title, title.Capacity);
                if (ConnectTitles.Contains(title.ToString().Trim(), StringComparer.OrdinalIgnoreCase)) open = true;
            }
            return !open;
        }, 0);
        return open;
    }

    private delegate bool EnumProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int max);
}
