using System.Reflection;

namespace Astro.Core;

/// <summary>
/// 제품 이름. 값은 저장소의 product.json에서 빌드할 때 들어온다 (Directory.Build.props).
/// 이름이 바뀌어도 코드는 고치지 않는다 — product.json만 고친다.
/// 문장에 넣을 때는 조사가 이름에 맞게 바뀌도록 Ga·Reul·Neun·Wa를 쓴다 (예: $"{Product.Ga} 켭니다").
/// </summary>
public static class Product
{
    private static readonly Dictionary<string, string> Meta = typeof(Product).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .ToDictionary(a => a.Key, a => a.Value ?? "");

    public static string Name { get; } = Get("AppProductName", "AA");
    public static string FullName { get; } = Get("AppProductFullName", "Astrophoto Assistant");
    public static string Tagline { get; } = Get("AppProductTagline", "");

    /// <summary>영문 짧은 이름 (실행 파일 이름 등)</summary>
    public static string Id { get; } = Get("AppProductId", Name);

    /// <summary>%LOCALAPPDATA% 아래 데이터 폴더 이름. 이름을 바꿔도 기존 데이터를 찾도록 따로 둔다.</summary>
    public static string DataFolder { get; } = Get("AppDataFolder", "AA");

    /// <summary>
    /// 이름을 바꾸기 전 데이터 폴더(product.json previousDataFolder)가 있고 새 폴더가 없으면 통째로 옮긴다 — 프로필·망원경·장소·이어서 하기 기록이 그대로 남게.
    /// 앱이 데이터를 읽기 전에 한 번 부른다 (데스크톱 시작, 서버 Build). 옮기지 못하면(다른 프로그램이 쓰는 중 등) 복사한다
    /// </summary>
    public static void MoveDataFromPreviousName()
    {
        var old = Get("AppPreviousDataFolder", "");
        if (old.Length == 0 || old.Equals(DataFolder, StringComparison.OrdinalIgnoreCase)) return;
        MoveData(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), old),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolder));
    }

    /// <summary>from 폴더를 to로 (to가 이미 있으면 아무것도 하지 않음 — 새 이름으로 한 번이라도 켠 뒤). 시험용으로 경로를 받는다</summary>
    public static void MoveData(string from, string to)
    {
        lock (MoveGate)
        {
            if (!Directory.Exists(from) || Directory.Exists(to)) return;
            try { Directory.Move(from, to); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { CopyDir(from, to); }
        }
    }

    private static readonly object MoveGate = new();

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
            try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: false); } catch (IOException) { }
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>"AA가" / "별빛이"</summary>
    public static string Ga => Name + Josa.Pick(Name, "이", "가");
    /// <summary>"AA를" / "별빛을"</summary>
    public static string Reul => Name + Josa.Pick(Name, "을", "를");
    /// <summary>"AA는" / "별빛은"</summary>
    public static string Neun => Name + Josa.Pick(Name, "은", "는");
    /// <summary>"AA와" / "별빛과"</summary>
    public static string Wa => Name + Josa.Pick(Name, "과", "와");

    private static string Get(string key, string fallback) =>
        Meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}

/// <summary>앞말의 받침 유무에 따라 조사를 고른다. 영문·숫자는 읽는 소리로 판단한다.</summary>
public static class Josa
{
    public static string Pick(string word, string withFinal, string withoutFinal) =>
        HasFinalConsonant(word) ? withFinal : withoutFinal;

    public static bool HasFinalConsonant(string word)
    {
        var s = word.TrimEnd();
        if (s.Length == 0) return false;
        var c = s[^1];
        if (c is >= '가' and <= '힣') return (c - '가') % 28 != 0;
        // 영문: 엘(L)·엠(M)·엔(N)·알(R)만 받침으로 끝난다
        if (char.IsAsciiLetter(c)) return "LMNRlmnr".Contains(c);
        // 숫자: 영·일·삼·육·칠·팔은 받침으로 끝난다
        if (char.IsAsciiDigit(c)) return "013678".Contains(c);
        return false;
    }
}
