using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core.Assistant;

namespace Astro.Server.Assistant.Providers;

/// <summary>
/// 연습 대화: AI 없이 정해진 순서로 묻는다 (대상 → 구도 → 필터·촬영 설정 → 끝난 뒤).
/// AI와 같은 도구를 불러서 계획 칸·그래프는 실제 계산 값으로 채워진다. AI 한도가 없을 때, 시연할 때 쓴다.
/// 대화 기록의 마지막(사용자 말 또는 도구 결과)을 보고 다음 할 일을 정한다 — 따로 상태를 두지 않는다.
/// </summary>
public sealed class ScriptedChatModel(Func<ShootingPlan> plan) : IChatModel
{
    public string Name => "연습 대화";

    private static readonly string[] FramingChoices = ["가운데", "왼쪽으로 조금 비켜서"];
    private static readonly string[] FilterChoices = ["필터 없음", "광해 필터", "듀얼 내로우밴드"];
    private static readonly string[] AfterChoices = ["기본값으로 하기"];

    /// <summary>한국어 통칭 → 번호 (연습 대화는 이름을 번역하지 못하므로 자주 찍는 대상만)</summary>
    private static readonly (string Korean, string Id)[] Aliases =
    [
        ("플레이아데스", "M45"), ("좀생이", "M45"), ("안드로메다", "M31"), ("오리온", "M42"), ("삼각형자리 은하", "M33"),
        ("북아메리카", "NGC 7000"), ("이중성단", "NGC 869"), ("소용돌이", "M51"), ("게성운", "M1"), ("고리", "M57"),
        ("아령", "M27"), ("독수리", "M16"), ("석호", "M8"), ("삼렬", "M20"), ("말머리", "B33"), ("불꽃", "NGC 2024"),
        ("장미", "NGC 2237"), ("하트", "IC 1805"), ("영혼", "IC 1848"), ("캘리포니아", "NGC 1499"), ("시가", "M82"),
        ("보데", "M81"), ("헤르쿨레스", "M13"), ("해바라기", "M63"), ("펠리컨", "IC 5070"), ("베일", "NGC 6960"),
        ("코끼리 코", "IC 1396"), ("나비", "IC 1318"), ("해파리", "IC 443"), ("바람개비", "M101"), ("솜브레로", "M104"),
    ];

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (text, calls) = Next(messages);
        // AI처럼 글이 조금씩 나오게
        foreach (var piece in Regex.Split(text, @"(?<= )"))
        {
            if (piece.Length == 0) continue;
            await Task.Delay(25, ct);
            yield return new TextDelta(piece);
        }
        var parts = new List<ChatPart>();
        if (text.Length > 0) parts.Add(new TextPart(text));
        parts.AddRange(calls);
        yield return new ChatCompleted(new ChatMessage(ChatRole.Assistant, parts));
    }

    private (string Text, List<ToolCallPart> Calls) Next(IReadOnlyList<ChatMessage> messages)
    {
        var last = messages[^1];
        var results = last.Parts.OfType<ToolResultPart>().ToList();
        return results.Count > 0 ? AfterTools(results, messages) : OnUser(last.Parts.OfType<TextPart>().FirstOrDefault()?.Text ?? "");
    }

    // ── 사용자가 말했을 때 ─────────────────────────

    private (string, List<ToolCallPart>) OnUser(string said)
    {
        var p = plan();
        // "대상 고치기" 등: 그 칸을 다시 묻는다
        var edit = Regex.Match(said, @"^(대상|구도|촬영 설정|끝난 뒤) 고치기$");
        if (edit.Success)
            return edit.Groups[1].Value switch
            {
                "대상" => ("어떤 대상으로 바꿀까요? 이름을 말하거나 추천을 볼 수 있어요.", [Offer("target", ["오늘 밤 추천 보기"])]),
                "구도" => ("대상을 화면 어디에 둘까요?", [Offer("framing", FramingChoices)]),
                "촬영 설정" => ("필터부터 다시 골라 주세요.", [Offer("settings", FilterChoices)]),
                _ => ("촬영이 끝난 뒤에는 어떻게 할까요?", [Offer("after", AfterChoices)]),
            };

        var step = p.Asking ?? (p.Target is null ? "target" : p.Framing is null ? "framing" : p.Settings is null ? "settings" : p.After is null ? "after" : "done");
        switch (step)
        {
            case "target":
                if (said.Contains("추천")) return ("", [Call("suggest_targets", new { limit = 4 })]);
                return ("", [Call("search_targets", new { query = TargetQuery(said) })]);
            case "framing":
                return ("", [Call("set_framing", new { placement = said.Length > 20 ? said[..20] : said })]);
            case "settings":
                return ("", [Call("recommend_settings", new { filter = FilterKey(said) })]);
            case "after":
                return ("", [Call("set_after", new { mount = "park", calibration = "none", disconnect = true })]);
            default:
                return ("계획이 모두 정해졌어요. 칸을 누르면 그 칸을 고칠 수 있고, 아래 버튼으로 준비를 시작할 수 있어요.", []);
        }
    }

    // ── 도구 결과를 받았을 때 ─────────────────────────

    private (string, List<ToolCallPart>) AfterTools(List<ToolResultPart> results, IReadOnlyList<ChatMessage> messages)
    {
        var r = results[0];
        var json = r.Result;
        if (json.TryGetProperty("error", out var err))
            return ($"잠깐 문제가 있었어요: {err.GetString()}", []);

        switch (r.Name)
        {
            case "offer_choices":
                return ("", []); // 질문은 이미 했다. 사용자 답을 기다린다

            case "suggest_targets":
            {
                var list = json.GetProperty("suggestions").EnumerateArray().ToList();
                if (list.Count == 0)
                    return ("오늘 밤 이 장비로 높이 뜨는 대상을 찾지 못했어요. 찍고 싶은 대상 이름을 말해 주세요.", [Offer("target", ["오늘 밤 추천 보기"])]);
                var chips = list.Select(Label).ToList();
                return ($"오늘 밤 오래 높이 뜨고 화면에 잘 맞는 대상이에요. {string.Join(", ", chips)} 중에 골라 보세요.",
                    [Offer("target", chips)]);
            }

            case "search_targets":
            {
                var list = json.GetProperty("results").EnumerateArray().ToList();
                if (list.Count == 0)
                    return ("그 이름으로는 찾지 못했어요. M31, NGC 7000처럼 번호로 말하거나 추천을 볼 수 있어요.", [Offer("target", ["오늘 밤 추천 보기"])]);
                var first = list[0];
                var id = first.GetProperty("id").GetString()!;
                return ("", [Call("set_target", new { target_id = id, korean_name = KoreanOf(first) ?? "" })]);
            }

            case "set_target":
            {
                var t = json.GetProperty("target");
                var name = Label(t);
                var above = t.GetProperty("above_30deg").GetString();
                var highest = t.GetProperty("highest").GetString();
                var fill = t.TryGetProperty("fill_percent_of_frame", out var f) && f.ValueKind == JsonValueKind.Number ? $" 화면 가로의 약 {f.GetDouble():F0}%를 채워요." : "";
                var sep = t.GetProperty("moon_separation_degrees").GetDouble();
                var moon = sep < 30 ? $" 오늘은 달과 {sep:F0}°로 가까워서 배경이 밝을 수 있어요." : "";
                var hi = highest!.Split(' ', 2);
                var when = above!.Contains("넘지 않음") ? "오늘 밤에는 30°를 넘지 않아요." : $"{above}에 30°보다 높고, 가장 높을 때는 {hi[0]}({(hi.Length > 1 ? hi[1] : "")})예요.";
                return ($"{name}로 정했어요. {when}{fill}{moon} 화면 어디에 둘까요?", [Offer("framing", FramingChoices)]);
            }

            case "set_framing":
                return ("좋아요. 오늘 경통에 어떤 필터를 끼우셨나요?", [Offer("settings", FilterChoices)]);

            case "recommend_settings":
            {
                var exposure = json.GetProperty("exposure_seconds").GetInt32();
                var iso = json.GetProperty("iso").GetInt32();
                var said = LastUserText(messages);
                var filter = FilterChoices.FirstOrDefault(said.Contains) ?? "필터 없음";
                return ("", [Call("set_settings", new
                {
                    exposure_seconds = exposure, iso, filter, end = "target_low_or_dawn",
                    exposure_recommended = true, iso_recommended = true,
                })]);
            }

            case "set_settings":
            {
                var p = plan().Settings;
                var line = p is null ? "" : $"노출은 {p.ExposureSeconds}초, 감도는 ISO {p.Iso}이에요. 대상이 낮아지거나 새벽이 올 때까지 약 {p.EstimatedFrames}장 찍어요.";
                return ($"{line} 끝나면 적도의를 파킹하고 장비 연결을 해제할게요. 다크·플랫은 찍지 않아요. 이대로 할까요?", [Offer("after", AfterChoices)]);
            }

            case "set_after":
                return ("모두 정해졌어요. 오른쪽 계획을 확인해 주세요.", []);

            default:
                return ("", []);
        }
    }

    // ── 도우미 ─────────────────────────

    private static int _seq;

    private static ToolCallPart Call(string name, object args) =>
        new($"s{Interlocked.Increment(ref _seq)}", name, JsonSerializer.SerializeToElement(args));

    private static ToolCallPart Offer(string field, IEnumerable<string> choices) =>
        Call("offer_choices", new { field, choices = choices.ToArray() });

    /// <summary>"M31 안드로메다" 같은 칩 이름: 번호 + 한국어 통칭(알면) 또는 영문 통칭</summary>
    private static string Label(JsonElement t)
    {
        var name = t.GetProperty("name").GetString();
        var korean = KoreanOf(t);
        var common = t.TryGetProperty("common_name", out var c) ? c.GetString() : null;
        return $"{name} {korean ?? common ?? ""}".Trim();
    }

    private static string? KoreanOf(JsonElement t)
    {
        var name = t.GetProperty("name").GetString() ?? "";
        return Aliases.Where(a => string.Equals(a.Id.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Korean).FirstOrDefault();
    }

    /// <summary>사용자 말에서 찾을 이름을 뽑는다: 번호가 있으면 번호, 아니면 한국어 통칭 → 번호, 아니면 말 그대로</summary>
    private static string TargetQuery(string said)
    {
        var id = Regex.Match(said, @"\b(M|NGC|IC|C|Sh2-|B)\s?\d+", RegexOptions.IgnoreCase);
        if (id.Success) return id.Value;
        foreach (var (korean, target) in Aliases)
            if (said.Contains(korean)) return target;
        return Regex.Replace(said, @"(을|를|이|가)?\s*(찍고 싶어요?|찍을래요?|보고 싶어요?|해 ?줘요?|요)$", "").Trim();
    }

    private static string FilterKey(string said) =>
        said.Contains("내로우") || said.Contains("듀얼") ? "dual_narrowband" : said.Contains("광해") ? "light_pollution" : "none";

    private static string LastUserText(IReadOnlyList<ChatMessage> messages) =>
        messages.LastOrDefault(m => m.Role == ChatRole.User && m.Parts.OfType<TextPart>().Any())?.Parts.OfType<TextPart>().First().Text ?? "";
}
