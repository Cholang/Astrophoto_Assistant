using System.Text.Json;
using Astro.Core.Assistant;
using Astro.Core.Sky;
using Astro.Server.Sky;

namespace Astro.Server.Assistant;

/// <summary>오늘 밤 계산에 필요한 것 한 묶음 (한 대화 동안 그대로)</summary>
public sealed record NightContext(
    Site Site, Rig Rig, NightWindow Window, MoonNight Moon, IReadOnlyList<CloudHour> Clouds, TimeZoneInfo Zone);

/// <summary>
/// AI에게 쥐여 주는 도구. 숫자(시각·고도·화각·장수)는 모두 여기서 계산하고, AI는 결과를 읽어 대화만 한다.
/// 계획 칸은 set_* 도구로만 바뀐다. 도구 설명은 모든 AI 회사가 같이 쓴다.
/// </summary>
public sealed class PlanTools(DsoCatalog catalog, Engine.EquipmentChoices equipment)
{
    public const double FrameOverheadSeconds = 40; // 사진 한 장마다 내려받기·저장·디더링으로 쉬는 시간 (2026-10-08 실기 X-T5: 약 38초)

    public static readonly IReadOnlyList<ToolSpec> Specs =
    [
        Spec("get_tonight", "오늘 밤 조건: 어두운 시간, 달(밝기·떠 있는 시간), 어두운 시간 동안 시간별 구름양, 촬영 장비(화각 포함).",
            """{"type":"object","properties":{}}"""),
        Spec("search_targets", "대상을 이름으로 찾는다. 'M45', 'NGC 7000', 'IC 434', 'Sh2-155', 'C14', 영문 통칭('Pleiades', 'Orion Nebula')을 넣는다. 한국어 이름은 영문 통칭이나 번호로 바꿔서 넣을 것. 오늘 밤 보이는 시간과 화각에 차지하는 크기도 함께 돌려준다.",
            """{"type":"object","properties":{"query":{"type":"string","description":"찾을 이름"}},"required":["query"]}"""),
        Spec("suggest_targets", "오늘 밤 이 장비로 찍기 좋은 대상을 고른다 (메시에·칼드웰 중 오래 높이 뜨고 화각에 잘 맞고 달과 먼 것).",
            """{"type":"object","properties":{"kind":{"type":"string","enum":["all","galaxy","nebula","cluster"],"description":"종류 제한"},"limit":{"type":"integer","description":"개수 (기본 4)"}}}"""),
        Spec("set_target", "계획의 '대상' 칸을 정한다. 사용자가 대상을 고르거나 동의했을 때만 부른다.",
            """{"type":"object","properties":{"target_id":{"type":"string","description":"search_targets·suggest_targets가 돌려준 id"},"korean_name":{"type":"string","description":"한국어 통칭 (예: 플레이아데스). 없으면 비움"}},"required":["target_id"]}"""),
        Spec("set_framing", "계획의 '구도' 칸을 정한다.",
            """{"type":"object","properties":{"placement":{"type":"string","description":"사용자 말 그대로 짧게 (예: 가운데, 왼쪽으로 조금 비켜서)"},"rotation_degrees":{"type":"number","description":"사용자가 카메라 방향 각도를 직접 말했을 때만 넣는다(사진 위쪽이 북쪽 = 0). 말하지 않았으면 넣지 않는다 — 지금 카메라 방향을 그대로 쓴다"}},"required":["placement"]}"""),
        Spec("recommend_settings", "노출 시간·ISO 추천값 (단순 표 기준: 초점비, 필터, 달 밝기). 촬영 설정을 묻기 전에 부른다.",
            """{"type":"object","properties":{"filter":{"type":"string","enum":["none","light_pollution","dual_narrowband"],"description":"필터 종류"}},"required":["filter"]}"""),
        Spec("set_settings", "계획의 '촬영 설정' 칸을 정한다. 예상 장수는 서버가 계산한다.",
            """{"type":"object","properties":{"exposure_seconds":{"type":"integer"},"iso":{"type":"integer"},"filter":{"type":"string","description":"화면에 쓸 필터 이름 (예: 필터 없음, 광해 필터, 듀얼 내로우밴드)"},"end":{"type":"string","enum":["target_low_or_dawn","dawn","time"],"description":"끝나는 조건. 기본은 target_low_or_dawn"},"end_time":{"type":"string","description":"end가 time일 때 HH:mm"},"exposure_recommended":{"type":"boolean","description":"노출이 추천값 그대로면 true"},"iso_recommended":{"type":"boolean","description":"ISO가 추천값 그대로면 true"}},"required":["exposure_seconds","iso","filter","end"]}"""),
        Spec("set_after", "계획의 '끝난 뒤' 칸을 정한다.",
            """{"type":"object","properties":{"mount":{"type":"string","enum":["park","leave"],"description":"적도의: 파킹 / 그대로"},"calibration":{"type":"string","enum":["none","darks","flats","darks_and_flats"],"description":"끝난 뒤 다크·플랫"},"disconnect":{"type":"boolean","description":"장비 연결 해제"}},"required":["mount","calibration","disconnect"]}"""),
        Spec("offer_choices", "사용자에게 질문할 때 부른다: 지금 묻는 칸을 표시하고, 답 아래에 누를 수 있는 선택지를 보여 준다. 선택지는 2~4개, 각 12자 이내.",
            """{"type":"object","properties":{"field":{"type":"string","enum":["target","framing","settings","after"]},"choices":{"type":"array","items":{"type":"string"}}},"required":["field","choices"]}"""),
    ];

    private static ToolSpec Spec(string name, string description, string schema) =>
        new(name, description, JsonDocument.Parse(schema).RootElement.Clone());

    /// <summary>도구 실행. 계획이 바뀌면 plan을 고치고, 선택지는 choices로 돌려준다.</summary>
    public JsonElement Run(string name, JsonElement args, NightContext night, ShootingPlan plan, out IReadOnlyList<string>? choices)
    {
        choices = null;
        try
        {
            object result = name switch
            {
                "get_tonight" => Tonight(night, Guiding(night)),
                "search_targets" => Search(Str(args, "query") ?? "", night),
                "suggest_targets" => Suggest(Str(args, "kind") ?? "all", Int(args, "limit") ?? 4, night),
                "set_target" => SetTarget(Str(args, "target_id") ?? "", Str(args, "korean_name"), night, plan),
                "set_framing" => SetFraming(args, night, plan),
                "recommend_settings" => Recommend(Str(args, "filter") ?? "none", night, Guiding(night)),
                "set_settings" => SetSettings(args, night, plan),
                "set_after" => SetAfter(args, plan),
                "offer_choices" => Offer(args, plan, out choices),
                _ => new { error = $"없는 도구입니다: {name}" },
            };
            return JsonSerializer.SerializeToElement(result, Json);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return JsonSerializer.SerializeToElement(new { error = e.Message }, Json);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Hm(DateTimeOffset? t) => t?.ToString("HH:mm") ?? "-";

    /// <summary>가이딩을 쓰는지: 프로필에 가이더가 있고, 장비 연결에서 "없이 진행"을 고르지 않았을 때</summary>
    private bool Guiding(NightContext n) => n.Rig.HasGuider && !equipment.IsWithout("guider");

    private static object Tonight(NightContext n, bool guiding)
    {
        var (fw, fh) = n.Rig.FieldOfView;
        return new
        {
            dark = new { start = Hm(n.Window.DarkStart), end = Hm(n.Window.DarkEnd), astronomical = n.Window.AstronomicalDark },
            sunset = Hm(n.Window.Sunset),
            sunrise = Hm(n.Window.Sunrise),
            moon = new
            {
                illumination_percent = (int)Math.Round(n.Moon.Illumination * 100),
                up = n.Moon.UpIntervals.Select(u => $"{Hm(u.Start)}-{Hm(u.End)}").ToList(),
            },
            clouds_during_dark = n.Clouds.Where(c => c.Time >= n.Window.DarkStart.AddHours(-1) && c.Time <= n.Window.DarkEnd)
                .Select(c => new { time = Hm(c.Time), cover_percent = c.Cover }).ToList(),
            clouds_available = n.Clouds.Count > 0,
            rig = new
            {
                telescope = n.Rig.Telescope,
                focal_length_mm = n.Rig.FocalLength,
                focal_ratio = n.Rig.FocalRatio,
                camera = n.Rig.Camera,
                field_of_view_degrees = $"{fw:F1} x {fh:F1}",
                pixel_scale_arcsec = Math.Round(n.Rig.PixelScale, 2),
                filter_wheel = n.Rig.HasFilterWheel,
                guiding,
            },
        };
    }

    private object Describe(Dso d, NightContext n)
    {
        var t = Night.Target(d.Position, n.Site, n.Window);
        return new
        {
            id = d.Id,
            name = d.Name,
            common_name = d.CommonName,
            type = d.Type,
            constellation = d.Constellation,
            magnitude = d.Magnitude,
            size_arcmin = d.SizeArcmin is { } s ? Math.Round(s, 1) : (double?)null,
            fill_percent_of_frame = Fill(d.SizeArcmin, n.Rig),
            above_30deg = t.UsableStart is null ? "오늘 밤 어두운 시간에는 30°를 넘지 않음" : $"{Hm(t.UsableStart)}-{Hm(t.UsableEnd)}",
            highest = $"{Hm(t.HighestAt)} {t.HighestAltitude:F0}°",
            transit = Hm(t.Transit),
            moon_separation_degrees = Math.Round(t.MoonSeparation),
        };
    }

    /// <summary>대상이 화면 긴 변에서 차지하는 비율(%)</summary>
    public static double? Fill(double? sizeArcmin, Rig rig) =>
        sizeArcmin is { } s && rig.FieldOfView.Width > 0 ? Math.Round(s / 60 / rig.FieldOfView.Width * 100) : null;

    private object Search(string query, NightContext n)
    {
        var found = catalog.Find(query);
        return found.Count == 0
            ? new { results = Array.Empty<object>(), note = "찾지 못했습니다. 번호(M, NGC, IC)나 영문 통칭으로 다시 찾아보세요." }
            : new { results = found.Select(d => Describe(d, n)).ToList(), note = "" };
    }

    private object Suggest(string kind, int limit, NightContext n)
    {
        var fovDeg = n.Rig.FieldOfView.Width;
        var bright = n.Moon.Illumination > 0.5;
        var scored = catalog.Showpieces()
            .Where(d => kind switch
            {
                "galaxy" => d.Type is "은하" or "은하단",
                "nebula" => d.Type.Contains("성운") || d.Type == "초신성 잔해",
                "cluster" => d.Type.Contains("성단"),
                _ => true,
            })
            .Select(d => (Dso: d, Night: Night.Target(d.Position, n.Site, n.Window)))
            .Where(x => x.Night.UsableStart is not null)
            .Select(x =>
            {
                var hours = (x.Night.UsableEnd!.Value - x.Night.UsableStart!.Value).TotalHours;
                var fill = (x.Dso.SizeArcmin ?? 1) / 60 / fovDeg; // 화면 긴 변 대비
                var fit = fill switch { < 0.05 => 0.2, < 0.15 => 0.6, <= 0.9 => 1.0, <= 1.3 => 0.7, _ => 0.3 };
                var moon = x.Night.MoonSeparation < 30 && bright ? 0.5 : 1.0;
                var mag = x.Dso.Magnitude is { } m ? Math.Clamp((12 - m) / 8, 0.2, 1) : 0.5;
                return (x.Dso, Score: hours * fit * moon * mag);
            })
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(limit, 1, 8))
            .Select(x => Describe(x.Dso, n))
            .ToList();
        return new { suggestions = scored, moon_bright = bright };
    }

    private object SetTarget(string id, string? korean, NightContext n, ShootingPlan plan)
    {
        if (catalog.Get(id) is not { } d) return new { error = "그 id의 대상이 없습니다. search_targets로 먼저 찾으세요." };
        var t = Night.Target(d.Position, n.Site, n.Window);
        plan.Target = new PlanTarget(d.Id, d.Name, d.CommonName, string.IsNullOrWhiteSpace(korean) ? null : korean.Trim(),
            d.Type, d.Constellation, d.SizeArcmin, d.Magnitude, d.Position.Ra, d.Position.Dec,
            t.UsableStart, t.UsableEnd, t.HighestAt, t.HighestAltitude, t.Transit, t.MoonSeparation);
        // 대상이 바뀌면 구도·설정은 다시 정한다
        if (plan.Framing is not null) plan.Framing = plan.Framing with { FillPercent = Fill(d.SizeArcmin, n.Rig) };
        if (plan.Settings is { } s) plan.Settings = Recount(s, n, plan.Target);
        ClearAsking(plan, "target");
        return new { ok = true, target = Describe(d, n) };
    }

    private static object SetFraming(JsonElement a, NightContext n, ShootingPlan plan)
    {
        if (plan.Target is null) return new { error = "대상을 먼저 정하세요." };
        // 각도를 말하지 않았으면 null = 지금 카메라 방향 유지 (0도로 돌리라는 뜻이 되지 않게, CX-PLAN-02)
        plan.Framing = new PlanFraming(Str(a, "placement") ?? "가운데", Num(a, "rotation_degrees"), Fill(plan.Target.SizeArcmin, n.Rig));
        ClearAsking(plan, "framing");
        return new { ok = true };
    }

    private static object Recommend(string filter, NightContext n, bool guiding)
    {
        // 단순 표 (DESIGN.md: 기록이 쌓이면 발전). 밝은 광학계일수록 짧게, 좁은 대역 필터는 길게
        var f = n.Rig.FocalRatio > 0 ? n.Rig.FocalRatio : 5;
        var exposure = f <= 4.5 ? 180 : f <= 6 ? 240 : 300;
        var iso = 800;
        var notes = new List<string>();
        if (filter == "dual_narrowband") { exposure = Math.Min(exposure * 2, 600); iso = 1600; notes.Add("좁은 대역 필터는 빛이 적어 길게"); }
        else if (filter == "light_pollution") { exposure = (int)(exposure * 1.3 / 30) * 30; }
        if (!guiding && exposure > 120) { exposure = 120; notes.Add("가이딩이 없어 짧게"); }
        if (filter != "dual_narrowband" && n.Moon.Illumination > 0.5 && n.Moon.UpIntervals.Count > 0)
        {
            exposure = Math.Max(60, (int)(exposure * 0.67 / 30) * 30);
            notes.Add("달이 밝아 배경이 빨리 밝아지므로 짧게");
        }
        return new { exposure_seconds = exposure, iso, notes };
    }

    private object SetSettings(JsonElement a, NightContext n, ShootingPlan plan)
    {
        if (plan.Target is null) return new { error = "대상을 먼저 정하세요." };
        var rule = Str(a, "end") switch { "dawn" => EndRule.Dawn, "time" => EndRule.AtTime, _ => EndRule.TargetLowOrDawn };
        var endTime = Str(a, "end_time");
        if (rule == EndRule.AtTime && !TimeOnly.TryParse(endTime, out _)) return new { error = "end가 time이면 end_time을 HH:mm로 넣으세요." };
        // 카메라가 쓸 수 없는 ISO면 계획에 넣지 않고 AI에게 범위를 알려 다시 고르게 한다 (Codex C06 — 조용히 다른 값으로 찍지 않게)
        if (Int(a, "iso") is { } wantIso && n.Rig.IsoMin is { } isoMin && n.Rig.IsoMax is { } isoMax && (wantIso < isoMin || wantIso > isoMax))
            return new { error = $"이 카메라({n.Rig.Camera})는 ISO {isoMin}~{isoMax}만 쓸 수 있습니다. 그 안의 값으로 다시 정하고, 사용자에게 바꾼 이유를 한 줄로 말하세요." };
        var s = new PlanSettings(Int(a, "exposure_seconds") ?? 180, Int(a, "iso") ?? 800, Str(a, "filter") ?? "필터 없음",
            rule, n.Window.DarkStart, n.Window.DarkEnd, 0,
            Bool(a, "exposure_recommended") ?? false, Bool(a, "iso_recommended") ?? false);
        plan.Settings = Recount(s, n, plan.Target, endTime);
        ClearAsking(plan, "settings");
        var r = plan.Settings;
        return new { ok = true, start = Hm(r.Start), end = Hm(r.End), estimated_frames = r.EstimatedFrames };
    }

    /// <summary>시작·끝 시각과 예상 장수를 다시 계산 (종료 규칙은 문장이 아니라 EndRule로, CX-PLAN-04). endTime은 규칙이 AtTime으로 새로 정해질 때만</summary>
    private static PlanSettings Recount(PlanSettings s, NightContext n, PlanTarget t, string? endTime = null)
    {
        // 오늘 밤 30° 위로 오지 않는 대상: 찍을 시간이 없다 (밤 전체를 촬영 시간으로 잡지 않게, CX-PLAN-04)
        if (t.UsableStart is null) return s with { Start = n.Window.DarkStart, End = n.Window.DarkStart, EstimatedFrames = 0 };
        var start = t.UsableStart is { } us && us > n.Window.DarkStart ? us : n.Window.DarkStart;
        var stop = s.EndRule switch
        {
            EndRule.Dawn => n.Window.DarkEnd,
            EndRule.AtTime when TimeOnly.TryParse(endTime, out var tt) => AtLocal(n, tt),
            EndRule.AtTime => s.End, // 정해 둔 시각 그대로
            _ => t.UsableEnd is { } ue && ue < n.Window.DarkEnd ? ue : n.Window.DarkEnd,
        };
        var seconds = Math.Max(0, (stop - start).TotalSeconds);
        var frames = (int)(seconds / (s.ExposureSeconds + FrameOverheadSeconds));
        return s with { Start = start, End = stop, EstimatedFrames = frames };
    }

    private static DateTimeOffset AtLocal(NightContext n, TimeOnly t)
    {
        // 밤 기준: 정오 이전 시각이면 다음 날
        var day = DateOnly.FromDateTime(n.Window.Sunset.DateTime);
        if (t.Hour < 12) day = day.AddDays(1);
        var local = day.ToDateTime(t);
        return new DateTimeOffset(local, n.Zone.GetUtcOffset(local));
    }

    private static object SetAfter(JsonElement a, ShootingPlan plan)
    {
        var cal = Str(a, "calibration") switch
        {
            "darks" => CalibrationFrames.Darks,
            "flats" => CalibrationFrames.Flats,
            "darks_and_flats" => CalibrationFrames.DarksAndFlats,
            _ => CalibrationFrames.None,
        };
        plan.After = new PlanAfter(Str(a, "mount") != "leave", cal, Bool(a, "disconnect") ?? true);
        ClearAsking(plan, "after");
        return new { ok = true };
    }

    private static object Offer(JsonElement a, ShootingPlan plan, out IReadOnlyList<string>? choices)
    {
        plan.Asking = Str(a, "field");
        choices = a.TryGetProperty("choices", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(4).ToList()
            : [];
        return new { ok = true, note = "선택지를 화면에 보여 주었습니다. 이제 질문 글을 쓰고 사용자의 답을 기다리세요." };
    }

    private static void ClearAsking(ShootingPlan plan, string field)
    {
        if (plan.Asking == field) plan.Asking = null;
    }

    private static string? Str(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
    private static double? Num(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static bool? Bool(JsonElement a, string k) => a.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
