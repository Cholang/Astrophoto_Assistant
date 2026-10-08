using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// 하늘 화면에 보낼 실제 이미지 (실장비 모드). 종류마다 마지막 한 장: sharpcap(SharpCap 화면 영상) · guide(PHD2 사진) · photo(솔빙 사진) · test(시험 사진).
/// 화면은 /api/prepare/live/{kind}로 받는다. sharpcap은 요청할 때마다 새로 캡처, guide는 PHD2가 지금 사진을 저장(save_image)하게 해 읽는다(2초에 한 번).
/// </summary>
public sealed class LiveImages(Phd2Client phd2)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (byte[] Bytes, string Type, DateTimeOffset At)> _images = [];

    public void Set(string kind, byte[] bytes, string contentType = "image/png")
    {
        lock (_gate) _images[kind] = (bytes, contentType, DateTimeOffset.Now);
    }

    public async Task<(byte[] Bytes, string Type, DateTimeOffset At)?> GetAsync(string kind, CancellationToken ct)
    {
        // SharpCap: 스크립트가 저장한 영상만 (창 캡처는 메뉴·패널까지 나와 하늘이 작게 보임 — 2026-10-08 실기). 저장본이 없을 때만 창 캡처
        if (kind == "sharpcap" && (SharpCapBridge.LatestView() ?? WindowCapture.Capture("SharpCap")) is { } shot) Set("sharpcap", shot);
        if (kind == "guide") await RefreshGuideAsync(ct);
        lock (_gate) return _images.TryGetValue(kind, out var v) ? v : null;
    }

    private async Task RefreshGuideAsync(CancellationToken ct)
    {
        lock (_gate)
            if (_images.TryGetValue("guide", out var g) && DateTimeOffset.Now - g.At < TimeSpan.FromSeconds(2)) return;
        try
        {
            var r = await phd2.CallAsync("save_image", ct: ct);
            if (r.ValueKind == System.Text.Json.JsonValueKind.Object && r.TryGetProperty("filename", out var f) && f.GetString() is { } path)
            {
                if (Fits.ToPng(path) is { } png) Set("guide", png);
                try { File.Delete(path); } catch (IOException) { }
            }
        }
        catch (Phd2Exception) { /* PHD2가 사진을 찍고 있지 않으면 save_image가 실패 — 마지막 사진 그대로 */ }
    }
}

/// <summary>
/// 다른 프로그램 창을 그대로 캡처 (PrintWindow + PW_RENDERFULLCONTENT — 2026-10-04 실기에서 흰 화면 문제를 이걸로 해결).
/// 창이 다른 창 뒤에 있어도 되지만 최소화돼 있으면 비어 나온다 → SharpCap은 최소화하지 않는다.
/// </summary>
public static class WindowCapture
{
    /// <summary>그 프로세스의 주 창을 PNG로 (가로 최대 1280으로 줄임). 창이 없으면 null</summary>
    public static byte[]? Capture(string processName, int maxWidth = 1280)
    {
        if (!OperatingSystem.IsWindows()) return null;
        nint hwnd = 0;
        var all = Process.GetProcessesByName(processName);
        try
        {
            foreach (var p in all)
                if (p.MainWindowHandle != 0) { hwnd = p.MainWindowHandle; break; }
        }
        finally
        {
            foreach (var p in all) p.Dispose(); // 찾은 뒤 남은 것까지 (break로 건너뛴 핸들이 새지 않게)
        }
        if (hwnd == 0 || IsIconic(hwnd)) return null;
        // 화면 배율(DPI)이 있으면 창 크기를 실제 픽셀로 받아야 창 전체가 잡힌다 (2026-10-06 실기: 왼쪽 위만 확대돼 나옴)
        var oldDpi = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        try { return CaptureWindow(hwnd, maxWidth); }
        finally { SetThreadDpiAwarenessContext(oldDpi); }
    }

    private static byte[]? CaptureWindow(nint hwnd, int maxWidth)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return null;

        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(dc, bmp);
        try
        {
            if (!PrintWindow(hwnd, dc, 2)) return null;
            var info = new BitmapInfo { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var bgra = new byte[w * h * 4];
            SelectObject(dc, old);
            if (GetDIBits(dc, bmp, 0, (uint)h, bgra, ref info, 0) == 0) return null;
            // 줄이기 (가까운 픽셀) → RGB
            var scale = Math.Max(1, (w + maxWidth - 1) / maxWidth);
            int ow = w / scale, oh = h / scale;
            var rgb = new byte[ow * oh * 3];
            for (var y = 0; y < oh; y++)
                for (var x = 0; x < ow; x++)
                {
                    var s = ((y * scale) * w + x * scale) * 4;
                    var d = (y * ow + x) * 3;
                    rgb[d] = bgra[s + 2];
                    rgb[d + 1] = bgra[s + 1];
                    rgb[d + 2] = bgra[s];
                }
            return Png.Encode(ow, oh, rgb);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(dc);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hWnd, nint hdc, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint hdc, nint bmp, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
}
