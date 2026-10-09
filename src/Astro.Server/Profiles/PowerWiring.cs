namespace Astro.Server.Profiles;

/// <summary>
/// 장비 전원 배선 (docs/POWER_LAYOUT_PLAN.md "최종 규칙", 2026-10-10 사용자 확정): 장비 종류마다 전원을 어디서 받는가.
/// 값 = "dc"(DC 전원) · "mount"(적도의 새들 포트) · "switch"(파워박스) · null(자체 전원 — 배터리 등).
/// 아이라는 이 값을 믿고 안내·판단하고, 실제와 다른 증거를 보면 조용히 고친다(<see cref="PowerWiringStore.Learn"/>).
/// 설정하지 않은 프로필은 <see cref="Default"/>(적도의 먼저). 장비 이름·전압·출력 번호는 담지 않는다
/// </summary>
public sealed record PowerWiring(Dictionary<string, string?> Sources)
{
    public const string Dc = "dc", Mount = "mount", Hub = "switch";

    /// <summary>장비 연결 단계의 장비 종류(망원경은 전원 없음) + 이슬 방지 열선. 순서 = 화면 위에서부터</summary>
    public static readonly string[] Kinds = [Mount, Hub, "camera", "focuser", "filterwheel", "rotator", "guider", "heater", "flatdevice"];

    /// <summary>기본: DC 전원 → 적도의 → 파워박스 → 나머지, 카메라는 자체 전원(배터리)</summary>
    public static PowerWiring Default => new(new Dictionary<string, string?>
    {
        [Mount] = Dc, [Hub] = Mount, ["camera"] = null, ["focuser"] = Hub, ["filterwheel"] = Hub,
        ["rotator"] = Hub, ["guider"] = Hub, ["heater"] = Hub, ["flatdevice"] = Hub,
    });

    /// <summary>그 장비가 전원을 받는 곳. 목록에 없는 종류(나중에 생긴 종류)는 파워박스 — 나중에 추가한 장비 규칙. 망원경 등 전원 없는 칸은 null</summary>
    public string? SourceOf(string kind) =>
        Sources.TryGetValue(kind, out var s) ? s : Kinds.Contains(kind) ? Default.Sources[kind] : null;

    /// <summary>파워박스를 거쳐 전원을 받는가 (파워박스에 전원이 없으면 같이 꺼지는 장비)</summary>
    public bool BehindHub(string kind)
    {
        var s = SourceOf(kind);
        for (var i = 0; i < 4 && s is not null and not Dc; i++)
        {
            if (s == Hub) return kind != Hub;
            s = SourceOf(s);
        }
        return false;
    }

    /// <summary>
    /// 한 장비의 전원을 바꾼다. 적도의 ↔ 파워박스 순서를 뒤집으면 앞이 되는 쪽이 DC 전원을 받는다 (서로에게서 받지 않게 — 화면의 잇기와 같은 규칙)
    /// </summary>
    public PowerWiring With(string kind, string? source)
    {
        var next = Kinds.ToDictionary(k => k, SourceOf);
        foreach (var (k, v) in Sources) next[k] = v;
        if (source is not null && next.GetValueOrDefault(source) == kind) next[source] = Dc;
        next[kind] = source;
        return new(next);
    }

    /// <summary>들어온 배선이 맞는지. 틀리면 이유</summary>
    public static string? Validate(IReadOnlyDictionary<string, string?> sources)
    {
        foreach (var (kind, source) in sources)
        {
            if (!Kinds.Contains(kind)) return $"모르는 장비 종류입니다 ({kind})";
            if (source is null) continue;
            if (source is not (Dc or Mount or Hub)) return $"모르는 전원입니다 ({source})";
            if (source == kind) return "자기 자신에게서 전원을 받을 수 없습니다";
            if (source == Dc && kind is not (Mount or Hub)) return "DC 전원은 적도의나 파워박스로만 이어집니다";
        }
        var w = new PowerWiring(new Dictionary<string, string?>(sources));
        foreach (var kind in Kinds)
        {
            var seen = new HashSet<string> { kind };
            for (var s = w.SourceOf(kind); s is not null and not Dc; s = w.SourceOf(s))
                if (!seen.Add(s)) return "적도의와 파워박스가 서로에게서 전원을 받을 수 없습니다";
        }
        return null;
    }

    /// <summary>모든 종류를 채운 표 (화면에 그대로 보낸다)</summary>
    public Dictionary<string, string?> Full() => Kinds.ToDictionary(k => k, SourceOf);
}

/// <summary>지금 프로필의 배선을 읽고, 아이라가 실제로 확인한 것으로 조용히 고친다 (알리지 않음 — 2026-10-10 사용자 결정)</summary>
public sealed class PowerWiringStore(ProfileStore profiles, ILogger<PowerWiringStore> log)
{
    public PowerWiring Current() => profiles.Current()?.Power ?? PowerWiring.Default;

    /// <summary>실제 관측으로 알게 된 배선. 이미 같으면 아무것도 하지 않는다. 고친 뒤로는 그 값을 믿는다</summary>
    public void Learn(string kind, string? source, string evidence)
    {
        if (profiles.Current() is not { } p) return;
        var now = p.Power ?? PowerWiring.Default;
        if (now.SourceOf(kind) == source) return;
        var next = now.With(kind, source);
        if (PowerWiring.Validate(next.Sources) is not null) return;
        profiles.SetPower(p.Id, next);
        log.LogInformation("전원 배선 고침 ({Profile}): {Kind} ← {Source} — {Evidence}", p.Nickname, kind, source ?? "자체 전원", evidence);
    }
}
