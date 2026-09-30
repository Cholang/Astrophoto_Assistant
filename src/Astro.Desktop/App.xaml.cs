using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Astro.Core;

namespace Astro.Desktop;

/// <summary>
/// 한 PC에서 하나만 켠다. 이미 켜져 있으면 새로 켜지 않고 켜져 있는 창을 앞으로 가져온다
/// (같은 주소·같은 NINA를 두 창이 나눠 쓰면 안 되므로).
/// </summary>
public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(initiallyOwned: true, $@"Local\{Product.DataFolder}-single-instance", out var first);
        if (!first)
        {
            BringExistingToFront();
            Shutdown();
            return;
        }
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    private static void BringExistingToFront()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            using (other)
            {
                if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, SwRestore);
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
