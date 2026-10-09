using System.Text.Json;
using Astro.Core;

namespace Astro.Server.Profiles;

/// <summary>
/// 넷플릭스식 사용자 프로필 (사람 단위). 장비 구성과는 별개. Sites: 이 사람의 관측지 목록 (최대 10개, 없던 예전 파일은 null).
/// Power: 장비 전원 배선 (null = 설정 안 함 → 기본값 적도의 먼저)
/// </summary>
public sealed record Profile(string Id, string Nickname, string? Memo, bool HasImage, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, List<ObservingSite>? Sites = null, PowerWiring? Power = null);

/// <summary>관측지 하나: 이름 + 위도·경도(도) + 고도(m)</summary>
public sealed record ObservingSite(string Id, string Name, double Latitude, double Longitude, double Elevation);

public sealed record NewProfile(string? Nickname, string? Memo);

public sealed record NewSite(string? Name, double Latitude, double Longitude, double? Elevation);

/// <summary>
/// 프로필 목록은 %LOCALAPPDATA%\<데이터 폴더>\profiles.json, 이미지는 profile-images\{id}.webp에 둔다.
/// 서버 한 곳에서 관리하므로 PC 화면과 폰 화면이 같은 목록을 본다.
/// </summary>
public sealed class ProfileStore
{
    // 글자 수는 byte로 센다: 한글 등은 2, 영문·숫자·기호는 1 (web/src/profiles.ts의 textBytes와 같은 규칙).
    // 별명 20byte = 한글 10자, 메모 120byte = 한글 60자.
    public const int NicknameMaxBytes = 20;
    public const int MemoMaxBytes = 120;
    /// <summary>화면이 256×256 WebP로 줄여서 보내므로 넉넉한 상한.</summary>
    public const int ImageMaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly string _imageDir;
    private readonly Lock _gate = new();

    public ProfileStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "profiles.json");
        _imageDir = Path.Combine(root, "profile-images");
    }

    /// <summary>만든 순서대로. 화면은 이 순서로 왼쪽부터 놓는다.</summary>
    public IReadOnlyList<Profile> List()
    {
        lock (_gate)
            return Load().OrderBy(p => p.CreatedAt).ToList();
    }

    public static int TextBytes(string text) => text.Sum(c => c < 0x80 ? 1 : 2);

    /// <returns>만든 프로필, 또는 입력이 잘못됐을 때 오류 문구</returns>
    public (Profile? Profile, string? Error) Create(NewProfile input)
    {
        var nickname = input.Nickname?.Trim() ?? "";
        var memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        if (nickname.Length == 0) return (null, "별명을 입력해 주세요.");
        if (TextBytes(nickname) > NicknameMaxBytes) return (null, $"별명은 최대 {NicknameMaxBytes}byte(한글 {NicknameMaxBytes / 2}자)까지 쓸 수 있습니다.");
        if (memo is not null && TextBytes(memo) > MemoMaxBytes) return (null, $"메모는 최대 {MemoMaxBytes}byte(한글 {MemoMaxBytes / 2}자)까지 쓸 수 있습니다.");

        lock (_gate)
        {
            var all = Load();
            var now = DateTimeOffset.Now;
            // 만든 프로필로 바로 시작하므로 마지막 사용 시각도 같이 기록한다.
            var profile = new Profile(Guid.NewGuid().ToString("N"), nickname, memo, false, now, now);
            all.Add(profile);
            Save(all);
            return (profile, null);
        }
    }

    public bool Select(string id) => Update(id, p => p with { LastUsedAt = DateTimeOffset.Now });

    public const int SitesMax = 10;
    public const int SiteNameMaxBytes = 30;

    /// <summary>별명·메모 고치기 (프로필 선택 화면의 편집)</summary>
    public (Profile? Profile, string? Error) Edit(string id, NewProfile input)
    {
        var nickname = input.Nickname?.Trim() ?? "";
        var memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        if (nickname.Length == 0) return (null, "별명을 입력해 주세요.");
        if (TextBytes(nickname) > NicknameMaxBytes) return (null, $"별명은 최대 {NicknameMaxBytes}byte(한글 {NicknameMaxBytes / 2}자)까지 쓸 수 있습니다.");
        if (memo is not null && TextBytes(memo) > MemoMaxBytes) return (null, $"메모는 최대 {MemoMaxBytes}byte(한글 {MemoMaxBytes / 2}자)까지 쓸 수 있습니다.");
        Profile? changed = null;
        Update(id, p => changed = p with { Nickname = nickname, Memo = memo });
        return changed is null ? (null, "프로필을 찾지 못했습니다.") : (changed, null);
    }

    /// <summary>관측지 추가 (최대 10개). 고도는 부른 쪽이 채워 준다</summary>
    public (Profile? Profile, string? Error) AddSite(string id, string name, double latitude, double longitude, double elevation)
    {
        name = name.Trim();
        if (name.Length == 0) return (null, "관측지 이름을 입력해 주세요.");
        if (TextBytes(name) > SiteNameMaxBytes) return (null, $"관측지 이름은 최대 {SiteNameMaxBytes}byte(한글 {SiteNameMaxBytes / 2}자)까지 쓸 수 있습니다.");
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180 || (latitude == 0 && longitude == 0)) return (null, "좌표가 올바르지 않습니다.");
        Profile? changed = null;
        string? error = null;
        Update(id, p =>
        {
            var sites = p.Sites ?? [];
            if (sites.Count >= SitesMax)
            {
                error = $"관측지는 {SitesMax}개까지 저장할 수 있습니다. 하나를 지운 뒤 추가해 주세요.";
                return changed = p;
            }
            var site = new ObservingSite(Guid.NewGuid().ToString("N"), name, Math.Round(latitude, 6), Math.Round(longitude, 6), Math.Round(elevation));
            return changed = p with { Sites = [.. sites, site] };
        });
        if (error is not null) return (null, error);
        return changed is null ? (null, "프로필을 찾지 못했습니다.") : (changed, null);
    }

    /// <summary>관측지 이름 고치기 (좌표는 고치지 않는다 — 2026-10-01 사용자 결정)</summary>
    public (Profile? Profile, string? Error) RenameSite(string id, string siteId, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return (null, "관측지 이름을 입력해 주세요.");
        if (TextBytes(name) > SiteNameMaxBytes) return (null, $"관측지 이름은 최대 {SiteNameMaxBytes}byte(한글 {SiteNameMaxBytes / 2}자)까지 쓸 수 있습니다.");
        Profile? changed = null;
        Update(id, p => changed = p with { Sites = (p.Sites ?? []).Select(s => s.Id == siteId ? s with { Name = name } : s).ToList() });
        return changed is null ? (null, "프로필을 찾지 못했습니다.") : (changed, null);
    }

    public Profile? RemoveSite(string id, string siteId)
    {
        Profile? changed = null;
        Update(id, p => changed = p with { Sites = (p.Sites ?? []).Where(s => s.Id != siteId).ToList() });
        return changed;
    }

    /// <summary>전원 배선 저장 (프로필 화면에서, 또는 아이라가 실제로 확인해 고칠 때)</summary>
    public bool SetPower(string id, PowerWiring wiring) => Update(id, p => p with { Power = wiring });

    /// <summary>지금 쓰는 프로필 = 마지막으로 고른 프로필</summary>
    public Profile? Current()
    {
        lock (_gate) return Load().Where(p => p.LastUsedAt is not null).MaxBy(p => p.LastUsedAt);
    }

    public Profile? Get(string id)
    {
        lock (_gate) return Load().FirstOrDefault(p => p.Id == id);
    }

    public bool SaveImage(string id, byte[] webp)
    {
        lock (_gate)
        {
            if (Load().All(p => p.Id != id)) return false;
            Directory.CreateDirectory(_imageDir);
            File.WriteAllBytes(ImagePath(id), webp);
        }
        return Update(id, p => p with { HasImage = true });
    }

    public string? ImageFile(string id)
    {
        var path = ImagePath(id);
        return File.Exists(path) ? path : null;
    }

    private string ImagePath(string id) => Path.Combine(_imageDir, $"{Path.GetFileName(id)}.webp");

    private bool Update(string id, Func<Profile, Profile> change)
    {
        lock (_gate)
        {
            var all = Load();
            var i = all.FindIndex(p => p.Id == id);
            if (i < 0) return false;
            all[i] = change(all[i]);
            Save(all);
            return true;
        }
    }

    private List<Profile> Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(_file), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            // 파일이 깨졌으면 지우지 않고 옆에 남겨 두고 빈 목록으로 시작한다.
            File.Copy(_file, _file + ".broken", overwrite: true);
            return [];
        }
    }

    private void Save(List<Profile> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Json));
        File.Move(temp, _file, overwrite: true);
    }
}
