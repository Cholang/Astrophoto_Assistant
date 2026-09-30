using System.Text.Json;
using Astro.Core;

namespace Astro.Server.Engine;

/// <summary>
/// 경통(또는 렌즈) 하나. 초점거리(mm)는 필수, 구경(mm)과 F값은 둘 중 하나만 있으면 된다.
/// 리듀서·플래트너 배율은 선택 (없으면 1). 실제로 쓰는 값은 배율을 곱한 Effective…
/// </summary>
public sealed record Scope(string Id, string Name, double FocalLength, double? Aperture, double? FocalRatio, double? Reducer, double? Flattener)
{
    private double Factor => (Reducer is > 0 ? Reducer.Value : 1) * (Flattener is > 0 ? Flattener.Value : 1);
    public double EffectiveFocalLength => FocalLength * Factor;
    public double EffectiveFocalRatio => (FocalRatio is > 0 ? FocalRatio.Value : Aperture is > 0 ? FocalLength / Aperture.Value : 0) * Factor;
    /// <summary>화면 표시: "260mm · f/3.8"</summary>
    public string Optics => $"{Math.Round(EffectiveFocalLength)}mm · f/{Math.Round(EffectiveFocalRatio, 1)}";
}

public sealed record NewScope(string? Name, double FocalLength, double? Aperture, double? FocalRatio, double? Reducer, double? Flattener);

/// <summary>
/// 경통 목록 (DESIGN.md 3장 "경통"). 프로필과 관계없이 AA 전체에 하나 — 경통은 사람이 아니라 장비라서.
/// 장비 연결 화면의 "장비 변경"에서 고르고·추가·수정·삭제한다. 데이터 폴더의 optics.json.
/// 경통은 연결되는 장비가 아니므로 N.I.N.A.는 모른다 → 고른 경통의 초점거리·F값을 N.I.N.A. 프로필에 써 넣는다 (EquipmentConnector)
/// </summary>
public sealed class OpticsStore
{
    public const int Max = 10;
    public const int NameMaxBytes = 30;

    private sealed record Data(List<Scope> Scopes, string? CurrentId);

    private readonly string _file;
    private readonly Lock _gate = new();

    public OpticsStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "optics.json");
    }

    public (IReadOnlyList<Scope> Scopes, string? CurrentId) List()
    {
        lock (_gate)
        {
            var d = Load();
            return (d.Scopes, d.CurrentId);
        }
    }

    public Scope? Current
    {
        get
        {
            lock (_gate)
            {
                var d = Load();
                return d.Scopes.FirstOrDefault(s => s.Id == d.CurrentId);
            }
        }
    }

    /// <summary>추가하고 바로 지금 경통으로 고른다</summary>
    public (Scope? Scope, string? Error) Add(NewScope input)
    {
        if (Validate(input) is { } error) return (null, error);
        lock (_gate)
        {
            var d = Load();
            if (d.Scopes.Count >= Max) return (null, $"경통은 {Max}개까지 저장할 수 있습니다. 하나를 지운 뒤 추가해 주세요.");
            var scope = Make(Guid.NewGuid().ToString("N"), input);
            Save(new Data([.. d.Scopes, scope], scope.Id));
            return (scope, null);
        }
    }

    public (Scope? Scope, string? Error) Edit(string id, NewScope input)
    {
        if (Validate(input) is { } error) return (null, error);
        lock (_gate)
        {
            var d = Load();
            if (d.Scopes.All(s => s.Id != id)) return (null, "경통을 찾지 못했습니다.");
            var scope = Make(id, input);
            Save(d with { Scopes = d.Scopes.Select(s => s.Id == id ? scope : s).ToList() });
            return (scope, null);
        }
    }

    /// <summary>지우기. 지금 쓰는 경통은 지울 수 없다 (관측지와 같은 규칙)</summary>
    public string? Remove(string id)
    {
        lock (_gate)
        {
            var d = Load();
            if (d.CurrentId == id) return "지금 쓰는 경통은 지울 수 없습니다.";
            Save(d with { Scopes = d.Scopes.Where(s => s.Id != id).ToList() });
            return null;
        }
    }

    public bool Use(string id)
    {
        lock (_gate)
        {
            var d = Load();
            if (d.Scopes.All(s => s.Id != id)) return false;
            Save(d with { CurrentId = id });
            return true;
        }
    }

    private static string? Validate(NewScope s)
    {
        var name = s.Name?.Trim() ?? "";
        if (name.Length == 0) return "경통 이름을 입력해 주세요.";
        if (Profiles.ProfileStore.TextBytes(name) > NameMaxBytes) return $"경통 이름은 최대 {NameMaxBytes}byte(한글 {NameMaxBytes / 2}자)까지 쓸 수 있습니다.";
        if (s.FocalLength is not (> 0 and < 20000)) return "초점거리를 mm로 입력해 주세요.";
        if (s.Aperture is not > 0 && s.FocalRatio is not > 0) return "구경(mm)이나 F값 중 하나를 입력해 주세요.";
        if (s.Reducer is <= 0 or > 5 || s.Flattener is <= 0 or > 5) return "배율은 0보다 크고 5 이하로 입력해 주세요 (예: 0.8).";
        return null;
    }

    private static Scope Make(string id, NewScope s) =>
        new(id, s.Name!.Trim(), s.FocalLength, s.Aperture is > 0 ? s.Aperture : null, s.FocalRatio is > 0 ? s.FocalRatio : null,
            s.Reducer is > 0 ? s.Reducer : null, s.Flattener is > 0 ? s.Flattener : null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private Data Load()
    {
        try
        {
            return File.Exists(_file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_file), Json) ?? new([], null) : new([], null);
        }
        catch (JsonException)
        {
            return new([], null);
        }
    }

    private void Save(Data d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(d, Json));
        File.Move(temp, _file, overwrite: true);
    }
}
