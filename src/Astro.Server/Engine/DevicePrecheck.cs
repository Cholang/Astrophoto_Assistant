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
            {
                // 지금 보이는 USB 직렬 포트를 함께 알린다 — 번호가 바뀌었으면 사용자가 고를 수 있게. 번호만으로 그 장비라고 단정하지 않는다 (Codex B02·B03)
                var others = host.UsbSerialPorts().Where(p => !p.Port.Equals(com, StringComparison.OrdinalIgnoreCase)).ToList();
                var seen = others.Count == 0 ? "" : $" 지금 PC에 보이는 USB 직렬 포트: {string.Join(", ", others.Select(p => $"{p.Port}({p.Name})"))}.";
                return new(false, $"{role}의 USB 포트({com})가 PC에 보이지 않습니다",
                    (kind == "mount"
                        ? $"적도의 전원이 켜져 있는지, USB 케이블이 꽂혀 있는지 확인해 주세요. 다른 USB 구멍에 꽂았다면 적도의 드라이버 설정에서 {com} 대신 새 포트를 골라 주세요."
                        : $"{role} 전원과 USB 케이블을 확인해 주세요. 다른 USB 구멍에 꽂았다면 드라이버 설정에서 {com} 대신 새 포트를 골라 주세요.") + seen);
            }
        }

        // ② 제조사를 아는 USB 장치가 PC에 있나 (C01·E01). 가이더는 PHD2가 기억한 카메라로 (2026-10-09 — 장비 전원을 다 끈 채 연결하자
        //    PHD2가 "equipment failed to connect"를 내고 N.I.N.A. 알림이 남음)
        if ((kind == "guider" ? GuideCameraVendor(host.Phd2CameraName()) : UsbVendor(kind, id)) is { } usb)
        {
            if (!await WaitAsync(() => host.UsbPresent(usb.Vid), wait, ct))
                return new(false, $"{role}{(Josa(role) ? "이" : "가")} PC에 보이지 않습니다", usb.Fix);
        }
        return Result.Pass;
    }

    /// <summary>이 장비가 PC에 보이는지 바로 알 수 있는가 (드라이버 COM 포트나 USB 제조사를 앎). 모르면 CheckAsync는 그냥 통과라 증거가 되지 않는다</summary>
    public static bool CanSee(IHostDevices host, string kind, string id) =>
        host.DriverComPort(kind, id) is not null || (kind == "guider" ? GuideCameraVendor(host.Phd2CameraName()) : UsbVendor(kind, id)) is not null;

    /// <summary>연결 직후 점검 결과: Block = 이대로는 촬영할 수 없음(실패), 아니면 알리고 진행(경고)</summary>
    public sealed record Note(bool Block, string Message, string Fix);

    /// <summary>
    /// 사진 저장 폴더 (I01·I02·I03): 있고 쓸 수 있는가, 남은 공간, 동기화 폴더인가. 장당 약 76MB(X-T5 RAW → FITS, 10/08 실기)
    /// </summary>
    public static Note? Storage(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null; // 모름
        if (!Directory.Exists(folder))
            return new(true, $"사진 저장 폴더가 없습니다 ({folder})", "N.I.N.A. 옵션 → 이미징의 이미지 파일 경로를 있는 폴더로 바꾸거나 그 폴더를 만들어 주세요. 외장 디스크라면 연결돼 있는지 확인해 주세요.");
        try
        {
            var probe = Path.Combine(folder, $".aira-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(true, $"사진 저장 폴더에 쓸 수 없습니다 ({folder})", "폴더 권한과 디스크 상태를 확인하거나, N.I.N.A. 이미지 파일 경로를 다른 폴더로 바꿔 주세요.");
        }
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
            const long frame = 76L * 1024 * 1024;
            if (free < 2L * 1024 * 1024 * 1024)
                return new(true, $"사진 저장 디스크 공간이 부족합니다 (남은 {free / 1024 / 1024 / 1024.0:0.0}GB, 약 {free / frame}장)", "디스크를 비우거나 N.I.N.A. 이미지 파일 경로를 여유 있는 디스크로 바꿔 주세요.");
            if (free < 20L * 1024 * 1024 * 1024)
                return new(false, $"사진 저장 공간이 넉넉하지 않습니다 (남은 {free / 1024 / 1024 / 1024.0:0}GB, 약 {free / frame}장)", "오늘 밤 계획보다 적으면 디스크를 비워 두세요.");
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        if (folder.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || folder.Contains("Dropbox", StringComparison.OrdinalIgnoreCase) || folder.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
            return new(false, "사진 저장 폴더가 동기화 폴더 안에 있습니다", "장당 76MB를 밤새 올리느라 저장이 늦어지거나 파일이 잠길 수 있습니다. N.I.N.A. 이미지 파일 경로를 동기화 폴더 밖(예: D 드라이브의 Photo 폴더)으로 바꾸는 것을 권합니다.");
        return null;
    }

    /// <summary>플레이트 솔버 (H01): ASTAP이면 실행 파일과 별 목록(데이터베이스 *.1476·*.290)이 있는가. 센터링·대상 확인에 필요</summary>
    public static Note? Solver(string? type, string? astapPath)
    {
        if (!string.Equals(type, "ASTAP", StringComparison.OrdinalIgnoreCase)) return null; // 다른 솔버는 아직 모름
        if (string.IsNullOrWhiteSpace(astapPath) || !File.Exists(astapPath))
            return new(false, "플레이트 솔빙 프로그램(ASTAP)을 찾지 못했습니다", "ASTAP을 설치하고 N.I.N.A. 옵션 → 플레이트 솔빙에서 ASTAP 경로를 지정해 주세요. 없으면 대상 가운데 맞추기(센터링)를 할 수 없습니다.");
        var dir = Path.GetDirectoryName(astapPath)!;
        var hasDb = Directory.EnumerateFiles(dir, "*.1476").Any() || Directory.EnumerateFiles(dir, "*.290").Any();
        return hasDb ? null : new(false, "ASTAP 별 목록(데이터베이스)이 없습니다", "ASTAP 홈페이지에서 별 목록(D50 등)을 받아 ASTAP 폴더에 설치해 주세요. 없으면 센터링을 할 수 없습니다.");
    }

    /// <summary>
    /// 적도의에 저장된 관측지가 N.I.N.A.(아이라가 넣은 관측지)와 다른가 (Codex D02) — 다르면 대상 위치·자오선 반전 시각이 틀어진다.
    /// 0.05°(약 5km) 넘게 다르면 알린다. 값을 모르면(0·NaN) 확인하지 않는다
    /// </summary>
    public static Note? MountSite(double profileLat, double profileLon, double mountLat, double mountLon)
    {
        static bool Known(double v) => double.IsFinite(v) && v != 0;
        if (!Known(profileLat) || !Known(mountLat) || !Known(profileLon) || !Known(mountLon)) return null;
        if (Math.Abs(profileLat - mountLat) <= 0.05 && Math.Abs(profileLon - mountLon) <= 0.05) return null;
        return new(false, $"적도의에 저장된 관측지가 다릅니다 (적도의 {mountLat:0.00}°, {mountLon:0.00}° · 아이라 {profileLat:0.00}°, {profileLon:0.00}°)",
            "N.I.N.A.가 관측지를 맞출지 물으면 N.I.N.A.(아이라) 값을 적도의로 보내기를 골라 주세요. 다르면 대상 위치와 자오선 반전 시각이 틀어질 수 있습니다.");
    }

    /// <summary>실장비로 쓰는데 N.I.N.A.·PHD2 장비가 시뮬레이터 (A06) — 시험 구성일 수 있어 막지 않고 알린다</summary>
    public static Note? Simulator(string role, string name) =>
        name.Contains("Simulator", StringComparison.OrdinalIgnoreCase)
            ? new(false, $"{role}{(Josa(role) ? "이" : "가")} 시뮬레이터입니다 ({name})", "실제 장비로 촬영하려면 장비 변경에서 실제 장비를 골라 주세요. 시험 중이면 그대로 진행해도 됩니다.")
            : null;

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

    /// <summary>PHD2 카메라 이름으로 아는 USB 제조사 번호 (ToupTek 0547 — 10/09 실기 G3M662M, ZWO 03C3, QHY 1618). 모르면 확인하지 않음</summary>
    internal static (string Vid, string Fix)? GuideCameraVendor(string? camera)
    {
        if (camera is null) return null;
        var vid = camera.Contains("ToupTek", StringComparison.OrdinalIgnoreCase) ? "0547"
            : camera.Contains("ZWO", StringComparison.OrdinalIgnoreCase) ? "03C3"
            : camera.Contains("QHY", StringComparison.OrdinalIgnoreCase) ? "1618"
            : null;
        return vid is null ? null : (vid, "가이드 카메라 USB 케이블을 확인해 주세요. 전원 허브의 USB에 꽂혀 있으면 허브 전원과 USB 출력이 켜져 있는지도 확인해 주세요.");
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
        /// <summary>USB에 붙은 직렬 포트와 장치 이름 (블루투스 직렬 포트는 뺀다 — 장비가 아님)</summary>
        IReadOnlyList<(string Port, string Name)> UsbSerialPorts();
        bool UsbPresent(string vid);
        /// <summary>ASCOM 드라이버 설정의 COM 포트 (직렬 드라이버가 아니거나 모르면 null)</summary>
        string? DriverComPort(string kind, string id);
        DateTimeOffset? EmpireStartedAt();
        DateTimeOffset? LastUsbInsertedAt();
        void CloseEmpire();
        /// <summary>PHD2가 지금 프로필에서 쓰는 카메라 이름 (예: "ToupTek Camera"). 모르면 null</summary>
        string? Phd2CameraName() => null;
    }

    public sealed class WindowsHost : IHostDevices
    {
        /// <summary>PHD2 설정(레지스트리 HKCU\Software\StarkLabs\PHDGuidingV2)의 지금 프로필 → camera\LastMenuchoice</summary>
        public string? Phd2CameraName()
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\StarkLabs\PHDGuidingV2");
            if (root?.GetValue("currentProfile") is not { } profile) return null;
            using var cam = root.OpenSubKey($@"profile\{profile}\camera");
            return cam?.GetValue("LastMenuchoice") as string;
        }

        /// <summary>지금 있는 COM 포트 (SerialPort.GetPortNames와 같은 곳 — 레지스트리 SERIALCOMM)</summary>
        public IReadOnlyList<string> ComPorts()
        {
            if (!OperatingSystem.IsWindows()) return [];
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            return key is null ? [] : key.GetValueNames().Select(n => key.GetValue(n) as string).OfType<string>().ToList();
        }

        public IReadOnlyList<(string Port, string Name)> UsbSerialPorts()
        {
            if (!OperatingSystem.IsWindows()) return [];
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                var list = new List<(string, string)>();
                foreach (var o in s.Get())
                {
                    var name = o["Name"] as string ?? "";
                    if (!((o["PNPDeviceID"] as string ?? "").StartsWith("USB", StringComparison.OrdinalIgnoreCase))) continue;
                    if (Regex.Match(name, @"\((COM\d+)\)") is { Success: true } m) list.Add((m.Groups[1].Value, name[..m.Index].Trim()));
                }
                return list;
            }
            catch (ManagementException) { return []; }
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
