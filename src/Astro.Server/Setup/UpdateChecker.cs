using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core;
using Microsoft.Extensions.Options;

namespace Astro.Server.Setup;

/// <summary>
/// [임시] 화면 확인용: 인터넷에서 받은 최신 버전 대신 이 버전이 나온 것으로 본다 (예: {"nina": "3.3.0.1001"}).
/// 지금 PC가 모두 최신이라 알림을 볼 수 없어서 (2026-10-06 사용자 요청). 확인이 끝나면 비운다.
/// </summary>
public sealed class UpdateOptions
{
    public Dictionary<string, string> Pretend { get; set; } = [];
}

/// <summary>새 버전이 나온 프로그램 하나 (프로필 화면의 "새 버전" 서랍에 한 줄)</summary>
public sealed record UpdateInfo(string Id, string Name, string Installed, string Latest, string Url, string How);

/// <summary>
/// 새 버전 알림 (DESIGN.md "프로필 화면" — 새 버전). 업데이트를 권하지 않고 알리기만 한다.
/// 설치된 버전은 파일·레지스트리에서, 최신 버전은 인터넷에서 하루 한 번만 받아 데이터 폴더에 기억한다.
/// 확인에 실패하면(인터넷 없음 등) 아무것도 알리지 않는다. 쓸 수 없는 옛 버전은 여기가 아니라 설치 확인에서 막는다.
/// 대상: N.I.N.A. · Advanced API · ASCOM Platform · PHD2 (SharpCap은 확인 경로가 불안정해 뒤로 미룸)
/// </summary>
/// dataDir: 데이터 폴더 (App:DataDir, 비우면 %LOCALAPPDATA%\제품 폴더)
public sealed class UpdateChecker(IHttpClientFactory http, IOptions<NinaOptions> nina, IOptions<UpdateOptions> options, ILogger<UpdateChecker> log, string? dataDir)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string Folder => dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
    private string CacheFile => Path.Combine(Folder, "updates.json");
    private string DismissFile => Path.Combine(Folder, "updates-dismissed.json");

    /// <summary>지난번에 받은 최신 버전. CheckedOn이 오늘이면 다시 받지 않는다</summary>
    private sealed record Cache(DateOnly CheckedOn, Dictionary<string, string> Latest);

    /// <summary>알릴 것: 설치된 버전보다 새 버전이 있고, 사용자가 "다시 알리지 않기"하지 않은 것</summary>
    public async Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct)
    {
        try
        {
            var latest = new Dictionary<string, string>(await LatestAsync(ct));
            foreach (var (id, version) in options.Value.Pretend) latest[id] = version; // [임시]
            var dismissed = Load<Dictionary<string, string>>(DismissFile) ?? [];
            var list = new List<UpdateInfo>();
            var ninaVersion = InstallLocator.NinaExe(nina.Value.ExePath) is { } exe ? FileVersion(exe) : null;
            Add(list, latest, dismissed, "nina", "N.I.N.A.", ninaVersion, 4,
                "https://nighttime-imaging.eu/download/", "N.I.N.A. 공식 페이지에서 내려받아 설치합니다");
            Add(list, latest, dismissed, "advanced-api", "Advanced API", AdvancedApiVersion(), 4,
                "https://github.com/christian-photo/ninaAPI/releases", "N.I.N.A.의 플러그인(Plugins) 탭에서 업데이트합니다");
            Add(list, latest, dismissed, "ascom", "ASCOM Platform", InstallLocator.AscomPlatformVersion(), 3,
                "https://ascom-standards.org/Downloads/Index.htm", "ASCOM 공식 페이지에서 내려받아 설치합니다");
            Add(list, latest, dismissed, "phd2", "PHD2", Phd2Version(), 3,
                "https://openphdguiding.org/downloads/", "PHD2 공식 페이지에서 내려받아 설치합니다");
            return list;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogInformation(e, "새 버전 확인 실패 (알리지 않음)");
            return [];
        }
    }

    /// <summary>이 버전은 다시 알리지 않기 (더 새 버전이 나오면 다시 알린다)</summary>
    public void Dismiss(string id, string version)
    {
        var map = Load<Dictionary<string, string>>(DismissFile) ?? [];
        map[id] = version;
        Save(DismissFile, map);
    }

    private static void Add(List<UpdateInfo> list, Dictionary<string, string> latest, Dictionary<string, string> dismissed,
        string id, string name, string? installed, int parts, string url, string how)
    {
        if (installed is null || !latest.TryGetValue(id, out var newest)) return;
        if (Compare(newest, installed, parts) <= 0) return;
        if (dismissed.TryGetValue(id, out var skip) && Compare(newest, skip, parts) <= 0) return;
        list.Add(new UpdateInfo(id, name, Short(installed, parts), Short(newest, parts), url, how));
    }

    // ── 최신 버전 (하루 한 번) ─────────

    private async Task<Dictionary<string, string>> LatestAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (Load<Cache>(CacheFile) is { } c && c.CheckedOn == today) return c.Latest;

            var client = http.CreateClient("updates");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Product.Name}-update-check"); // GitHub API는 User-Agent가 있어야 한다
            var latest = new Dictionary<string, string>();
            var ninaVersion = InstallLocator.NinaExe(nina.Value.ExePath) is { } exe ? FileVersion(exe) : null;
            // 네 곳은 서로 상관없으니 한꺼번에 묻는다 — 가장 느린 한 곳만큼만 기다린다 (Codex 최적화 제안)
            var found = await Task.WhenAll(
                Try("nina", () => NinaLatestAsync(client, ct)),
                Try("advanced-api", () => AdvancedApiLatestAsync(client, ninaVersion, ct)),
                Try("ascom", () => GitHubLatestAsync(client, "ASCOMInitiative/ASCOMPlatform", ct)),
                Try("phd2", () => GitHubLatestAsync(client, "OpenPHDGuiding/phd2", ct)));
            foreach (var (id, version) in found)
                if (version is not null) latest[id] = version;
            // 하나도 받지 못했으면(인터넷 없음) 기억하지 않는다 — 다음 실행 때 다시
            if (latest.Count > 0) Save(CacheFile, new Cache(today, latest));
            return latest;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Id, string? Version)> Try(string id, Func<Task<string?>> get)
    {
        try
        {
            return (id, await get());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            log.LogInformation("새 버전 확인: {Id} 실패 ({Message})", id, e.Message);
            return (id, null);
        }
    }

    /// <summary>N.I.N.A. 자체 업데이트가 쓰는 정식판 정보</summary>
    private static async Task<string?> NinaLatestAsync(HttpClient client, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("https://nighttime-imaging.eu/wp-json/nina/v1/versioninfo/release", ct));
        return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
    }

    /// <summary>N.I.N.A. 플러그인 목록에서 Advanced API 중 설치된 N.I.N.A.에서 쓸 수 있는 가장 새 버전</summary>
    private static async Task<string?> AdvancedApiLatestAsync(HttpClient client, string? ninaVersion, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("https://nighttime-imaging.eu/wp-json/nina/v1/plugins/manifest", ct));
        string? best = null;
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            if (p.GetProperty("Name").GetString() != "Advanced API") continue;
            var v = Version(p.GetProperty("Version"));
            if (ninaVersion is not null && p.TryGetProperty("MinimumApplicationVersion", out var min) && Compare(Version(min), ninaVersion, 4) > 0) continue;
            if (best is null || Compare(v, best, 4) > 0) best = v;
        }
        return best;

        static string Version(JsonElement e) =>
            string.Join('.', new[] { "Major", "Minor", "Patch", "Build" }.Select(k => e.TryGetProperty(k, out var x) ? x.ToString() : "0"));
    }

    /// <summary>GitHub 릴리스 중 정식판(태그 v1.2.3, dev·beta 등 꼬리 없는 것)의 가장 새 버전</summary>
    private static async Task<string?> GitHubLatestAsync(HttpClient client, string repo, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=30", ct));
        string? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) continue;
            if (r.GetProperty("tag_name").GetString() is not { } tag || Regex.Match(tag, @"^v?(\d+\.\d+\.\d+)$") is not { Success: true } m) continue;
            if (best is null || Compare(m.Groups[1].Value, best, 3) > 0) best = m.Groups[1].Value;
        }
        return best;
    }

    // ── 설치된 버전 ─────────

    private static string? FileVersion(string path)
    {
        var v = FileVersionInfo.GetVersionInfo(path);
        return v.ProductVersion is { Length: > 0 } p ? p : v.FileVersion;
    }

    /// <summary>%LOCALAPPDATA%\NINA\Plugins\&lt;버전&gt;\Advanced API\ninaAPI.dll의 파일 버전 (여러 개면 가장 새 것)</summary>
    private static string? AdvancedApiVersion()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Plugins");
        if (!Directory.Exists(root)) return null;
        string? best = null;
        foreach (var dll in Directory.EnumerateFiles(root, "ninaAPI.dll", SearchOption.AllDirectories))
            if (FileVersion(dll) is { } v && (best is null || Compare(v, best, 4) > 0)) best = v;
        return best;
    }

    private static (DateTime Stamp, string? Version)? _phd2;

    /// <summary>PHD2는 실행 파일에 버전 정보가 없다 → 실행 파일 안의 "PHD2 Guiding 2.6.14" 글자를 읽는다 (파일이 바뀔 때만 다시)</summary>
    private static string? Phd2Version()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PHDGuiding2", "phd2.exe");
        if (!File.Exists(exe)) return null;
        var stamp = File.GetLastWriteTimeUtc(exe);
        if (_phd2 is { } c && c.Stamp == stamp) return c.Version;
        string? version;
        try { version = FindUtf16Version(exe, "PHD2 Guiding "); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { version = null; }
        _phd2 = (stamp, version);
        return version;
    }

    /// <summary>
    /// 파일에서 UTF-16 글자 prefix 바로 뒤의 버전(숫자·점)을 찾는다. 파일 전체를 문자열로 만들지 않고 1MB씩 읽는다
    /// (조각 경계에 걸쳐도 찾도록 앞 조각 끝을 조금 남겨 둠). 버전 형식은 a.b.c
    /// </summary>
    internal static string? FindUtf16Version(string path, string prefix)
    {
        var pattern = Encoding.Unicode.GetBytes(prefix);
        const int Tail = 64; // 버전 글자(최대 32자)를 읽을 여유
        var buffer = new byte[(1 << 20) + pattern.Length + Tail];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        var kept = 0;
        while (true)
        {
            var read = fs.Read(buffer, kept, buffer.Length - kept);
            var len = kept + read;
            var span = buffer.AsSpan(0, len);
            for (var from = 0; ;)
            {
                var i = span[from..].IndexOf(pattern);
                if (i < 0) break;
                i += from;
                var start = i + pattern.Length;
                if (start + Tail > len && read > 0) break; // 뒤 글자가 아직 다 안 읽힘 → 다음 조각에서
                var chars = Encoding.Unicode.GetString(span[start..Math.Min(len, start + Tail)]);
                if (Regex.Match(chars, @"^\d+\.\d+\.\d+") is { Success: true } m) return m.Value;
                from = i + 1;
            }
            if (read == 0) return null;
            // 끝부분을 남겨 다음 조각과 이어 찾는다
            kept = Math.Min(len, pattern.Length + Tail);
            span[(len - kept)..].CopyTo(buffer);
        }
    }

    // ── 도우미 ─────────

    /// <summary>앞 parts자리만 숫자로 비교 (ASCOM 7.1.3.4851 vs 태그 7.1.3 → 3자리)</summary>
    internal static int Compare(string a, string b, int parts)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        for (var i = 0; i < parts; i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    private static int[] Numbers(string v) =>
        Regex.Matches(v, @"\d+").Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();

    private static string Short(string v, int parts) => string.Join('.', Numbers(v).Take(parts));

    private static T? Load<T>(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static void Save<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(value, Json));
    }
}
