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
        ["light"] = "#F1F1EF",
        ["night"] = "#000000",
    };

    private static readonly string ThemeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "theme.txt");

    private WebApplication? _server;

    public MainWindow()
    {
        InitializeComponent();
        Title = string.IsNullOrEmpty(Product.Tagline) ? Product.Name : $"{Product.Name} · {Product.Tagline}";
        ApplyTheme(ReadSavedTheme());
        Loaded += OnLoaded;
        Closing += OnClosing;
        // 처음부터 전체화면 (제목 표시줄·작업 표시줄 없이). F11로 창 모드와 오간다. 끄기는 Alt+F4
        SetFullScreen(true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.F11) return;
            SetFullScreen(WindowStyle != WindowStyle.None);
            e.Handled = true;
        };
    }

    private void SetFullScreen(bool on)
    {
        // 작업 표시줄까지 덮으려면 테두리를 없앤 뒤에 최대화해야 한다 (순서 중요)
        if (on)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
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
                        Close();
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

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_server is null) return;
        var server = _server;
        _server = null;
        await server.StopAsync();
        await server.DisposeAsync();
    }
}
