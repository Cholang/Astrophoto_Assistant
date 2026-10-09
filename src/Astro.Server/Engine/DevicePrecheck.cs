using System.Diagnostics;

using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Astro.Server.Engine;

/// <summary>
/// 장비 연결 직전 점검 (docs/PRECHECK_DESIGN.md ③, 2026-10-09 사용자 요청 — N.I.N.A.가 오류를 내기 전에 아이라가 먼저 확인).
/// N.I.N.A.에 연결을 맡기기 전에 장비가 PC에 보이는지 본다. 안 보이면 N.I.N.A.를 부르지 않고(영어 오류·2분 기다림 없이) 할 일을 알린다.
/// 확인할 수 없으면 "모름"으로 그냥 진행한다 — 막는 것은 확실히 없을 때만 (Codex 원칙 1·2: 관측한 사실만, 원인은 후보로)
/// </summary>
public static class DevicePrecheck
{
    public sealed record Result(bool Ok, string? Message = null, string? Fix = null)
    {
        public static readonly Result Pass = new(true);
    }

    /// <summary>
    /// 연결 전에 볼 것. kind = camera·mount·focuser·…, id = 드라이버 Id(예: ASCOM.OnStep.Telescope, Fujifilm Camera Plugin_X-T5).
    /// USB 출력을 막 켠 직후일 수 있어 USB 장치는 잠깐(최대 wait) 기다려 본다
    /// </summary>
    public static async Task<Result> CheckAsync(IHostDevices host, string kind, string id, string role, TimeSpan wait, CancellationToken ct)
    {
        // ① 드라이버에 적힌 COM 포트가 PC에 있나 (B01) — 적도의는 전원이 꺼지면 USB 포트도 사라진다(10/09 실기)
        if (host.DriverComPort(kind, id) is { } com)
        {
            if (!await WaitAsync(() => host.ComPorts().Contains(com, StringComparer.OrdinalIgnoreCase), wait, ct))
                return new(false, $"{role}의 USB 포트({com})가 PC에 보이지 않습니다",
                    kind == "mount"
                        ? $"적도의 전원이 켜져 있는지, USB 케이블이 꽂혀 있는지 확인해 주세요. 다른 USB 구멍에 꽂았다면 적도의 드라이버 설정에서 {com} 대신 새 포트를 골라 주세요."
                        : $"{role} 전원과 USB 케이블을 확인해 주세요. 다른 USB 구멍에 꽂았다면 드라이버 설정에서 {com} 대신 새 포트를 골라 주세요.");
        }

        // ② 제조사를 아는 USB 장치가 PC에 있나 (C01·E01)
        if (UsbVendor(kind, id) is { } usb)
        {
            if (!await WaitAsync(() => host.UsbPresent(usb.Vid), wait, ct))
                return new(false, $"{role}{(Josa(role) ? "이" : "가")} PC에 보이지 않습니다", usb.Fix);
        }
        return Result.Pass;
    }

    /// <summary>드라이버 Id로 아는 USB 제조사 번호 (없으면 확인하지 않음)</summary>
    internal static (string Vid, string Fix)? UsbVendor(string kind, string id)
    {
        if (kind == "camera")
        {
            var vid = id.Contains("Fujifilm", StringComparison.OrdinalIgnoreCase) ? "04CB"
                : id.Contains("Canon", StringComparison.OrdinalIgnoreCase) ? "04A9"
                : id.Contains("Nikon", StringComparison.OrdinalIgnoreCase) ? "04B0"
                : id.Contains("Sony", StringComparison.OrdinalIgnoreCase) ? "054C"
                : null;
            return vid is null ? null : (vid, "카메라 전원(배터리 또는 전원 어댑터)을 켜고, USB 케이블과 카메라의 PC 연결 방식(USB 테더 촬영)을 확인해 주세요.");
        }
        if (kind == "focuser" && id.Contains("AOFocuser", StringComparison.OrdinalIgnoreCase))
            return ("338F", "포커서 USB 케이블을 확인해 주세요. 전원 허브의 USB 출력을 쓰면 그 출력이 켜져 있는지도 확인해 주세요.");
        return null;
    }

    private static bool Josa(string word) => Astro.Core.Josa.HasFinalConsonant(word);

    private static async Task<bool> WaitAsync(Func<bool> ok, TimeSpan wait, CancellationToken ct)
    {
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            if (ok()) return true;
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(500, ct);
        }
    }

    /// <summary>
    /// Wanderer Empire가 허브와 끊겼을 수 있나 (J01·J02): Empire가 켜진 뒤에 USB 장치가 새로 꽂혔으면 Empire는 허브와 다시 연결하지 않는다(10/09 실기 —
    /// N.I.N.A. 연결이 2분 기다린 뒤 실패). 그때는 Empire를 닫아 두면 N.I.N.A. 스위치 연결이 새로 띄우고 자동으로 연결한다(16초)
    /// </summary>
    public static bool EmpireStale(IHostDevices host)
    {
        if (host.EmpireStartedAt() is not { } started) return false;
        return host.LastUsbInsertedAt() is { } inserted && inserted > started;
    }

    /// <summary>윈도우 PC의 실제 장치·설정 (시험에서는 가짜로 바꾼다)</summary>
    public interface IHostDevices
    {
        IReadOnlyList<string> ComPorts();
        bool UsbPresent(string vid);
        /// <summary>ASCOM 드라이버 설정의 COM 포트 (직렬 드라이버가 아니거나 모르면 null)</summary>
        string? DriverComPort(string kind, string id);
        DateTimeOffset? EmpireStartedAt();
        DateTimeOffset? LastUsbInsertedAt();
        void CloseEmpire();
    }

    public sealed class WindowsHost : IHostDevices
    {
        /// <summary>지금 있는 COM 포트 (SerialPort.GetPortNames와 같은 곳 — 레지스트리 SERIALCOMM)</summary>
        public IReadOnlyList<string> ComPorts()
        {
            if (!OperatingSystem.IsWindows()) return [];
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            return key is null ? [] : key.GetValueNames().Select(n => key.GetValue(n) as string).OfType<string>().ToList();
        }

        public bool UsbPresent(string vid)
        {
            if (!OperatingSystem.IsWindows()) return true; // 모름 → 막지 않음
            try
            {
                using var s = new ManagementObjectSearcher($"SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\VID_{vid}%'");
                return s.Get().Count > 0;
            }
            catch (ManagementException) { return true; }
        }

        private static readonly Dictionary<string, string> AscomType = new()
        {
            ["mount"] = "Telescope", ["focuser"] = "Focuser", ["filterwheel"] = "FilterWheel", ["rotator"] = "Rotator", ["flatdevice"] = "CoverCalibrator",
        };

        public string? DriverComPort(string kind, string id)
        {
            if (!OperatingSystem.IsWindows() || !AscomType.TryGetValue(kind, out var type) || !id.StartsWith("ASCOM.", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var root in new[] { @"SOFTWARE\WOW6432Node\ASCOM", @"SOFTWARE\ASCOM" })
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{root}\{type} Drivers\{id}");
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                    if (name.Contains("port", StringComparison.OrdinalIgnoreCase) && key.GetValue(name) is string v && Regex.IsMatch(v.Trim(), @"^COM\d+$", RegexOptions.IgnoreCase))
                        return v.Trim().ToUpperInvariant();
            }
            return null;
        }

        public DateTimeOffset? EmpireStartedAt()
        {
            var ps = Process.GetProcessesByName(BackgroundWindows.WandererEmpire);
            try { return ps.Length == 0 ? null : ps.Min(p => (DateTimeOffset)p.StartTime); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
            finally { foreach (var p in ps) p.Dispose(); }
        }

        /// <summary>N.I.N.A. 로그의 마지막 "New USB device detected" 시각 (N.I.N.A.가 켜져 있는 동안의 USB 꽂힘)</summary>
        public DateTimeOffset? LastUsbInsertedAt()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");
            try
            {
                var f = new DirectoryInfo(folder).EnumerateFiles("*.log").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f is null) return null;
                using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(s);
                DateTimeOffset? last = null;
                while (r.ReadLine() is { } line)
                    if (line.Contains("UsbDeviceWatcher_DeviceInserted", StringComparison.Ordinal) && line.Length > 24 && DateTimeOffset.TryParse(line[..24], out var t))
                        last = t;
                return last;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return null; }
        }

        /// <summary>Empire와 그 ASCOM 서버를 닫는다 (N.I.N.A.가 허브를 쓰고 있지 않을 때만 부른다)</summary>
        public void CloseEmpire()
        {
            foreach (var name in new[] { BackgroundWindows.WandererEmpire, "ASCOM.WandererBox1", "ASCOM.WandererBox2" })
                foreach (var p in Process.GetProcessesByName(name))
                    using (p)
                    {
                        try
                        {
                            if (!p.CloseMainWindow() || !p.WaitForExit(5000)) p.Kill();
                            p.WaitForExit(5000);
                        }
                        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    }
        }
    }
}
