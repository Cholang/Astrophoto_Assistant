using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Astro.Core;
using Astro.Server;
using Microsoft.AspNetCore.Builder;

namespace Astro.Desktop;

/// <summary>
/// 얇은 창 껍데기. 코어 서버를 같은 프로세스에서 켜고, 그 화면을 WebView2로 보여 준다.
/// 폰에서 보는 화면과 같은 화면이다.
/// </summary>
public partial class MainWindow : Window
{
    // web/src/index.css의 --bg와 같은 값. 화면이 뜨기 전 순간에 보이는 색이다.
    private static readonly Dictionary<string, string> ThemeBackground = new()
    {
        ["dark"] = "#1E1F21",
        ["night"] = "#000000",
    };

    private static readonly string ThemeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "theme.txt");

    private WebApplication? _server;

    public MainWindow()
    {
        InitializeComponent();
        Title = Product.Name; // 한 줄 설명(tagline)은 2026-10-10 사용자 요청으로 없앰
        ApplyTheme(ReadSavedTheme());
        Loaded += OnLoaded;
        Closing += OnClosing;
        // 처음부터 전체화면 (제목 표시줄·작업 표시줄 없이). F11로 창 모드와 오간다. 끄기는 Alt+F4.
        // 창 핸들이 생긴 뒤에 모니터 크기를 잴 수 있어 SourceInitialized에서
        SourceInitialized += (_, _) => SetFullScreen(true);
        // 최소화했다 다시 열면 같은 자리·크기로
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal && WindowStyle == WindowStyle.None) FitMonitor(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F11) return;
            SetFullScreen(WindowStyle != WindowStyle.None);
            e.Handled = true;
        };
    }

    private void SetFullScreen(bool on)
    {
        // 테두리 없는 창을 "최대화"하면 WPF가 화면보다 조금 크게 잡아 오른쪽·아래가 화면 밖으로 밀린다 (2026-10-08 실기: 캡처에 왼쪽·위 경계만 보임)
        // → 최대화하지 않고, 지금 모니터 전체(작업 표시줄 포함) 크기에 정확히 맞춘다
        if (on)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            FitMonitor();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _server = AppServer.Build([], AppServer.DefaultUrl, AppContext.BaseDirectory);
            await _server.StartAsync();

            await Web.EnsureCoreWebView2Async();
            // 디스크 캐시를 비운다: 예전에 no-cache 없이 받아 둔 index.html이 남아 있으면 WebView2가 새 빌드 대신 옛 화면을 띄운다
            // (2026-10-09 이름을 바꾼 뒤 "AA v0.1.58" 화면이 뜸). 화면 파일은 내 PC의 내부 서버에서 오므로 비워도 느려지지 않는다. 설정(localStorage)은 남음
            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
            // 바깥 링크(설치 안내 등)는 창 안이 아니라 기본 브라우저로 연다.
            Web.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
            };
            // 화면에서 테마를 바꾸면 창 배경도 맞추고, 다음 실행을 위해 기억한다.
            Web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    // 화면 아래 단계 레일의 "앱 끄기" (전체화면이라 창 닫기 버튼이 없다)
                    if (type == "close")
                    {
                        _closeConfirmed = true; // 화면이 확인했다 (또는 묻지 않아도 되는 화면)
                        Close();
                        return;
                    }
                    // 상태 줄의 "창 내리기" (전체 화면이라 창 버튼이 없다)
                    if (type == "minimize")
                    {
                        WindowState = WindowState.Minimized;
                        return;
                    }
                    if (type != "theme") return;
                    var theme = doc.RootElement.GetProperty("theme").GetString();
                    if (theme is null || !ThemeBackground.ContainsKey(theme)) return;
                    ApplyTheme(theme);
                    SaveTheme(theme);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            };
            Web.Source = new Uri(AppServer.DefaultUrl);
        }
        catch (Exception ex)
        {
            // AA끼리는 App이 하나만 켜게 막는다. 여기 오는 건 다른 프로그램(개발용 서버 등)이 주소를 쓰고 있을 때
            var reason = ex is IOException && ex.Message.Contains("address already in use")
                ? $"다른 프로그램이 {AppServer.DefaultUrl} 주소를 쓰고 있습니다.\n개발용 서버(dotnet run --project src/Astro.Server)가 켜져 있으면 닫고 다시 실행해 주세요."
                : ex.Message;
            MessageBox.Show(this, $"{Product.Reul} 시작하지 못했습니다.\n\n{reason}", Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void ApplyTheme(string theme)
    {
        var color = (Color)ColorConverter.ConvertFromString(ThemeBackground[theme]);
        Background = new SolidColorBrush(color);
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
    }

    private static string ReadSavedTheme()
    {
        try
        {
            var t = File.ReadAllText(ThemeFile).Trim();
            return ThemeBackground.ContainsKey(t) ? t : "dark";
        }
        catch (IOException) { return "dark"; }
        catch (UnauthorizedAccessException) { return "dark"; }
    }

    private static void SaveTheme(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, theme);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 창 닫기: 내부 서버를 끝까지 멈춘 뒤에 닫는다. async void라 await에서 창이 먼저 닫히고 프로세스가 끝나
    /// 서버의 정리(N.I.N.A. 감시 등 백그라운드 작업 종료·기록)가 잘릴 수 있었다 → 닫기를 한 번 미루고, 멈춘 뒤 다시 닫는다
    /// </summary>
    /// <summary>창을 지금 모니터 전체 크기에 맞춘다 (화면 배율을 고려해 픽셀 → WPF 단위로)</summary>
    private void FitMonitor()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* 가장 가까운 모니터 */), ref info)) return;
        var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = toDip.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        var bottomRight = toDip.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect32 Monitor; public Rect32 Work; public uint Flags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private bool _stopping, _stopped;

    // 창의 × 버튼·Alt+F4: 진행 중이면 화면에 "종료할까요?"를 띄운다 (2026-10-08 사용자 결정). 화면이 답("close")하면 닫는다
    private bool _closeConfirmed;

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_closeConfirmed && !_stopping && !_stopped && Web.CoreWebView2 is { } web)
        {
            e.Cancel = true;
            web.PostWebMessageAsJson("{\"type\":\"confirm-close\"}");
            return;
        }
        if (_stopped) return;
        if (_stopping) { e.Cancel = true; return; } // 멈추는 중에 다시 누름
        if (_server is null) return;
        e.Cancel = true;
        _stopping = true;
        var server = _server;
        _server = null;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 멈추지 않아도 창은 닫히게
            await server.StopAsync(limit.Token);
            await server.DisposeAsync();
        }
        catch (Exception) { /* 닫는 중 — 더 할 수 있는 일이 없다 */ }
        _stopped = true;
        Close();
    }
}
