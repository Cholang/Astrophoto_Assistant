using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AA", "theme.txt");

    private WebApplication? _server;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(ReadSavedTheme());
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _server = AaServer.Build([], AaServer.DefaultUrl, AppContext.BaseDirectory);
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
                    if (doc.RootElement.GetProperty("type").GetString() != "theme") return;
                    var theme = doc.RootElement.GetProperty("theme").GetString();
                    if (theme is null || !ThemeBackground.ContainsKey(theme)) return;
                    ApplyTheme(theme);
                    SaveTheme(theme);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            };
            Web.Source = new Uri(AaServer.DefaultUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"AA를 시작하지 못했습니다.\n\n{ex.Message}", "AA", MessageBoxButton.OK, MessageBoxImage.Error);
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
