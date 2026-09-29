using Microsoft.Win32;

namespace Astro.Server.Setup;

/// <summary>Windows에 설치된 프로그램 위치를 찾는다 (레지스트리 + 기본 설치 경로).</summary>
public static class InstallLocator
{
    public static string? AscomPlatformVersion()
    {
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\ASCOM\Platform", @"SOFTWARE\ASCOM\Platform" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key?.GetValue("Platform Version") is string v && v.Length > 0) return v;
        }
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "ASCOM", "Platform");
        return Directory.Exists(folder) ? "버전 확인 불가" : null;
    }

    /// <summary>
    /// NINA 실행 파일. 반드시 파일 이름이 NINA.exe인 것만 돌려준다.
    /// 주의: 설치 정보의 DisplayIcon은 설치 프로그램(Package Cache\...\NINASetupBundle.exe)을 가리킬 수 있다.
    /// 그걸 실행하면 "Modify Setup"(Repair/Uninstall) 창이 뜬다 — 절대 실행하면 안 된다.
    /// </summary>
    public static string? NinaExe(string? configuredPath)
    {
        if (IsNinaExe(configuredPath)) return configuredPath;

        var programFiles = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "N.I.N.A. - Nighttime Imaging 'N' Astronomy", "NINA.exe"));
        foreach (var path in programFiles)
            if (IsNinaExe(path)) return path;

        foreach (var entry in UninstallEntries())
        {
            if (!entry.DisplayName.StartsWith("N.I.N.A", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.InstallLocation is { Length: > 0 } dir && IsNinaExe(Path.Combine(dir, "NINA.exe"))) return Path.Combine(dir, "NINA.exe");
            if (entry.DisplayIcon is { } icon && icon.Split(',')[0].Trim('"') is var exe && IsNinaExe(exe)) return exe;
        }
        return null;
    }

    private static bool IsNinaExe(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetFileName(path), "NINA.exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    /// <summary>
    /// NINA 플러그인은 %LOCALAPPDATA%\NINA\Plugins\&lt;NINA 버전&gt;\&lt;플러그인 이름&gt;에 설치된다.
    /// NINA를 켜지 않고도 설치 여부를 알 수 있다 (켜져 있는지·포트는 1단계에서 확인).
    /// </summary>
    public static bool AdvancedApiPluginInstalled()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Plugins");
        if (!Directory.Exists(root)) return false;
        return Directory.EnumerateDirectories(root).Any(version =>
            Directory.Exists(Path.Combine(version, "Advanced API")) || File.Exists(Path.Combine(version, "ninaAPI.dll")));
    }

    public static bool Phd2Installed(string? profilePath)
    {
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)) return true;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PHDGuiding2", "phd2.exe");
        return File.Exists(fallback) || UninstallEntries().Any(e => e.DisplayName.StartsWith("PHD2", StringComparison.OrdinalIgnoreCase));
    }

    public static bool AstapInstalled(string? profilePath)
    {
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)) return true;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "astap", "astap.exe");
        return File.Exists(fallback) || UninstallEntries().Any(e => e.DisplayName.StartsWith("ASTAP", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record UninstallEntry(string DisplayName, string? InstallLocation, string? DisplayIcon);

    private static IEnumerable<UninstallEntry> UninstallEntries()
    {
        var roots = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in roots)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string displayName) continue;
                yield return new UninstallEntry(displayName, key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string);
            }
        }
    }
}
