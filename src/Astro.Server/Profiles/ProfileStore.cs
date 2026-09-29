using System.Text.Json;

namespace Astro.Server.Profiles;

/// <summary>넷플릭스식 사용자 프로필 (사람 단위). 장비 구성과는 별개.</summary>
public sealed record Profile(string Id, string Nickname, string? Memo, bool HasImage, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record NewProfile(string? Nickname, string? Memo);

/// <summary>
/// 프로필 목록은 %LOCALAPPDATA%\AA\profiles.json, 이미지는 profile-images\{id}.webp에 둔다.
/// 서버 한 곳에서 관리하므로 PC 화면과 폰 화면이 같은 목록을 본다.
/// </summary>
public sealed class ProfileStore
{
    public const int NicknameMaxLength = 20;
    public const int MemoMaxLength = 60;
    /// <summary>화면이 256×256 WebP로 줄여서 보내므로 넉넉한 상한.</summary>
    public const int ImageMaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly string _imageDir;
    private readonly Lock _gate = new();

    public ProfileStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AA");
        _file = Path.Combine(root, "profiles.json");
        _imageDir = Path.Combine(root, "profile-images");
    }

    /// <summary>마지막으로 쓴 프로필이 맨 앞.</summary>
    public IReadOnlyList<Profile> List()
    {
        lock (_gate)
            return Load().OrderByDescending(p => p.LastUsedAt ?? p.CreatedAt).ToList();
    }

    /// <returns>만든 프로필, 또는 입력이 잘못됐을 때 오류 문구</returns>
    public (Profile? Profile, string? Error) Create(NewProfile input)
    {
        var nickname = input.Nickname?.Trim() ?? "";
        var memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        if (nickname.Length == 0) return (null, "닉네임을 입력해 주세요.");
        if (nickname.Length > NicknameMaxLength) return (null, $"닉네임은 {NicknameMaxLength}자까지 쓸 수 있습니다.");
        if (memo?.Length > MemoMaxLength) return (null, $"메모는 {MemoMaxLength}자까지 쓸 수 있습니다.");

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
