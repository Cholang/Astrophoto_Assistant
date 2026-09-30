using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Astro.Core;
using Astro.Core.Assistant;
using Astro.Core.Sky;
using Astro.Server.Assistant.Providers;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server.Assistant;

public sealed class AssistantOptions
{
    /// <summary>gemini · scripted(연습 대화, AI 없음). 새 회사를 붙이면 여기에 이름을 더한다 (ChatModelFactory)</summary>
    public string Provider { get; set; } = "gemini";

    /// <summary>앞에서부터 시도한다. 과부하면 다음 모델로</summary>
    public string[] Models { get; set; } = [];

    /// <summary>생각의 깊이: low · medium · high. 회사마다 이름이 달라 번역기가 맞춘다 (대화는 low면 충분하고 빠르다)</summary>
    public string Effort { get; set; } = "low";

    /// <summary>회사별 키. appsettings.json에는 두지 않고 사용자 비밀 저장소·환경 변수에서만 읽는다</summary>
    public Dictionary<string, string> ApiKeys { get; set; } = [];
}

/// <summary>설정의 Provider 값으로 AI 구현을 고른다. 회사를 바꾸려면 설정 한 줄만 바꾼다.</summary>
public sealed class ChatModelFactory(IOptions<AssistantOptions> options, IHttpClientFactory httpFactory)
{
    /// <summary>실제 AI를 쓸 수 없는 이유 (설정이 연습 대화이거나 키가 없음). 쓸 수 있으면 null</summary>
    public string? PracticeReason
    {
        get
        {
            var o = options.Value;
            if (o.Provider == "scripted") return "지금은 연습 대화입니다. AI 없이 정해진 순서로 묻습니다.";
            if (string.IsNullOrWhiteSpace(o.ApiKeys.GetValueOrDefault(o.Provider))) return "AI 키가 없어 연습 대화로 진행합니다.";
            return null;
        }
    }

    public IChatModel Create()
    {
        var o = options.Value;
        var key = o.ApiKeys.GetValueOrDefault(o.Provider) ?? "";
        switch (o.Provider)
        {
            case "gemini":
                var http = httpFactory.CreateClient("assistant");
                http.Timeout = TimeSpan.FromSeconds(90);
                return new GeminiChatModel(http, key, o.Models.Length > 0 ? o.Models : ["gemini-flash-latest"], o.Effort);
            default:
                throw new ChatModelException($"모르는 AI 회사입니다: {o.Provider}", ChatFailure.Key);
        }
    }

    /// <summary>연습 대화 (AI 없음). 계획 칸은 같은 도구로 채운다</summary>
    public static IChatModel Practice(Func<ShootingPlan> plan) => new ScriptedChatModel(plan);
}

/// <summary>화면에 보여 줄 대화 한 줄 (도구 호출 등은 빼고 글만)</summary>
public sealed record DisplayMessage(string Role, string Text, IReadOnlyList<string>? Choices = null);

/// <summary>
/// 촬영 계획 대화 (한 사용자, 한 밤). AI에게 도구를 쥐여 주고, 도구 호출 → 실행 → 결과 돌려주기를 반복한다.
/// 이 반복·계획 상태·화면 이벤트는 AI 회사와 무관하다.
/// </summary>
public sealed class PlanAssistant(ChatModelFactory models, PlanTools tools, TonightService tonight, ILogger<PlanAssistant> log)
{
    private const int MaxRounds = 8;

    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly List<ChatMessage> _history = [];
    private readonly List<DisplayMessage> _display = [];
    private ShootingPlan _plan = new();
    private NightContext? _night;
    private DateOnly _evening;
    /// <summary>AI 한도가 다 되어 연습 대화로 넘어간 경우, 한도가 다시 차는 시각까지</summary>
    private DateTimeOffset _practiceUntil = DateTimeOffset.MinValue;
    private string _practiceReason = "";
    private bool _noticeShown;

    public ShootingPlan Plan => _plan;
    public NightContext? Night => _night;
    public IReadOnlyList<DisplayMessage> Display => _display;

    /// <summary>화면을 열 때: 오늘 밤 정보를 준비하고, 처음이면 인사를 만든다. 날짜가 바뀌면 새로 시작.</summary>
    public async Task<string?> EnsureStartedAsync(CancellationToken ct)
    {
        var evening = TonightService.EveningOf(DateTimeOffset.Now);
        if (_night is not null && _evening == evening) return null;

        if (await tonight.ReadProfileAsync(ct) is not { } profile)
            return "N.I.N.A. 프로필을 읽지 못했습니다. N.I.N.A.가 켜져 있는지 확인해 주세요.";
        if (profile.Site is { Latitude: 0, Longitude: 0 })
            return "N.I.N.A. 프로필에 관측지 위치가 없습니다. N.I.N.A.의 옵션 > 일반 > 천문 설정에서 위도·경도를 넣어 주세요.";

        var zone = TimeZoneInfo.Local;
        var window = Core.Sky.Night.Window(profile.Site, evening, zone);
        var moon = Core.Sky.Night.Moon(profile.Site, window);
        var clouds = await tonight.CloudsAsync(profile.Site, ct);
        _night = new NightContext(profile.Site, profile.Rig, window, moon, clouds, zone);
        _evening = evening;
        _history.Clear();
        _display.Clear();
        _plan = new ShootingPlan { Asking = "target" };
        _noticeShown = false;
        _display.Add(new DisplayMessage("assistant", Greeting(_night), ["오늘 밤 추천 보기"]));
        return null;
    }

    public void Reset()
    {
        _night = null;
    }

    /// <summary>사용자 말 하나를 처리한다. 화면으로 보낼 이벤트: notice · text · plan · choices · error · done</summary>
    public async IAsyncEnumerable<SseItem<object>> SendAsync(string text, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!await _busy.WaitAsync(0, ct))
        {
            yield return Event("error", new { message = "앞의 답을 쓰는 중입니다. 잠시 기다려 주세요." });
            yield break;
        }
        try
        {
            if (await EnsureStartedAsync(ct) is { } problem)
            {
                yield return Event("error", new { message = problem });
                yield break;
            }
            var night = _night!;
            _display.Add(new DisplayMessage("user", text));
            _history.Add(ChatMessage.User(text));

            // 실제 AI를 쓸 수 없으면 연습 대화 (설정·키 없음·오늘 한도 초과)
            var reason = models.PracticeReason ?? (DateTimeOffset.Now < _practiceUntil ? _practiceReason : null);
            var model = reason is null ? models.Create() : ChatModelFactory.Practice(() => _plan);
            if (reason is not null && !_noticeShown)
            {
                _noticeShown = true;
                _display.Add(new DisplayMessage("notice", reason));
                yield return Event("notice", new { message = reason });
            }
            var said = new StringBuilder();
            IReadOnlyList<string>? choices = null;

            for (var round = 0; round < MaxRounds; round++)
            {
                var channel = System.Threading.Channels.Channel.CreateUnbounded<SseItem<object>>();
                ChatMessage? reply = null;
                string? failure = null;
                var failureKind = ChatFailure.Other;

                // 응답 스트림을 읽는 동안 글 조각을 바로 화면으로 흘려 보낸다
                var pump = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var chunk in model.StreamAsync(SystemPrompt(night, _plan), _history, PlanTools.Specs, ct))
                        {
                            if (chunk is TextDelta d)
                            {
                                said.Append(d.Text);
                                await channel.Writer.WriteAsync(Event("text", new { text = d.Text }), ct);
                            }
                            else if (chunk is ChatCompleted c) reply = c.Message;
                        }
                    }
                    catch (ChatModelException e) { failure = e.Message; failureKind = e.Kind; log.LogWarning(e, "AI 호출 실패"); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        failure = "AI의 답을 받지 못했습니다. 잠시 뒤 다시 보내 주세요.";
                        log.LogWarning(e, "AI 응답 오류");
                    }
                    finally { channel.Writer.Complete(); }
                }, ct);

                await foreach (var item in channel.Reader.ReadAllAsync(ct)) yield return item;
                await pump;

                // 한도 초과·키 문제: 이 대화부터 연습 대화로 넘어가 같은 말을 이어서 처리한다
                if (failure is not null && failureKind is ChatFailure.Quota or ChatFailure.Key && model is not ScriptedChatModel)
                {
                    // 한도: 다시 차는 시각까지 / 키: 앱을 다시 켤 때까지 (키를 고치면 다시 켜야 하므로)
                    _practiceUntil = failureKind == ChatFailure.Quota ? NextQuotaReset() : DateTimeOffset.MaxValue;
                    _practiceReason = failureKind == ChatFailure.Quota
                        ? "오늘 AI 사용 한도를 다 써서 연습 대화로 진행합니다."
                        : "AI 키에 문제가 있어 연습 대화로 진행합니다.";
                    var notice = failureKind == ChatFailure.Quota
                        ? "AI 한도가 다 되어 연습 대화로 이어갑니다."
                        : "AI 키에 문제가 있어 연습 대화로 이어갑니다.";
                    _noticeShown = true;
                    _display.Add(new DisplayMessage("notice", notice));
                    yield return Event("notice", new { message = notice });
                    model = ChatModelFactory.Practice(() => _plan);
                    round--;
                    continue;
                }

                if (failure is not null || reply is null)
                {
                    // 실패한 사용자 말은 기록에서 빼서, 다시 보내면 처음처럼 이어지게 한다
                    if (round == 0 && _history.Count > 0 && _history[^1].Role == ChatRole.User) _history.RemoveAt(_history.Count - 1);
                    if (round == 0 && _display.Count > 0 && _display[^1].Role == "user") _display.RemoveAt(_display.Count - 1);
                    yield return Event("error", new { message = failure ?? "AI가 답하지 않았습니다." });
                    yield break;
                }

                _history.Add(reply);
                var calls = reply.Parts.OfType<ToolCallPart>().ToList();
                if (calls.Count == 0) break;

                var results = new List<ChatPart>();
                foreach (var call in calls)
                {
                    var result = tools.Run(call.Name, call.Arguments, night, _plan, out var offered);
                    log.LogInformation("도구 {Tool} {Args}", call.Name, call.Arguments.GetRawText());
                    results.Add(new ToolResultPart(call.Id, call.Name, result));
                    if (offered is not null)
                    {
                        choices = offered;
                        yield return Event("choices", new { choices });
                    }
                }
                yield return Event("plan", PlanView.From(_plan, night));
                _history.Add(new ChatMessage(ChatRole.User, results));
            }

            _display.Add(new DisplayMessage("assistant", said.ToString().Trim(), choices));
            yield return Event("done", new { });
        }
        finally
        {
            _busy.Release();
        }
    }

    private static SseItem<object> Event(string type, object data) => new(data, type);

    /// <summary>무료 한도가 다시 차는 시각: 미국 태평양 시각 자정 (한국 오후 4~5시)</summary>
    private static DateTimeOffset NextQuotaReset()
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, pacific);
        var midnight = now.Date.AddDays(1);
        return new DateTimeOffset(midnight, pacific.GetUtcOffset(midnight));
    }

    private static string Hm(DateTimeOffset t) => t.ToString("HH:mm");

    private static string Greeting(NightContext n)
    {
        var dark = $"오늘 밤은 {Hm(n.Window.DarkStart)}부터 {Hm(n.Window.DarkEnd)}까지 어두워요.";
        var moon = MoonLine(n);
        return $"{dark} {moon} 무엇을 찍어 볼까요?";
    }

    private static string MoonLine(NightContext n)
    {
        var pct = (int)Math.Round(n.Moon.Illumination * 100);
        if (pct < 8 || n.Moon.UpIntervals.Count == 0) return "달이 없는 밤이에요.";
        var (s, e) = n.Moon.UpIntervals[0];
        if (s <= n.Window.DarkStart && e >= n.Window.DarkEnd) return $"달({pct}%)이 밤새 떠 있어요.";
        if (s <= n.Window.DarkStart) return $"달({pct}%)은 {Hm(e)}에 져요.";
        return $"달({pct}%)은 {Hm(s)}에 떠요.";
    }

    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web);

    private static string SystemPrompt(NightContext n, ShootingPlan plan)
    {
        var state = JsonSerializer.Serialize(PlanView.From(plan, n), PlanJson);
        return $$"""
            너는 {{Product.Name}} 앱의 천체사진 촬영 비서다. 사용자와 대화해서 오늘 밤 촬영 계획 네 칸(대상, 구도, 촬영 설정, 끝난 뒤)을 채운다.
            화면 왼쪽이 대화, 오른쪽에 계획 네 칸과 오늘 밤 그래프가 있다. 화면은 이미 이렇게 인사했다: "{{Greeting(n)}}"

            말하기
            - 한국어 존댓말(~요), 한 번에 2~3문장. 목록·표·마크다운을 쓰지 않는다.
            - 시각·고도·화각·장수 같은 숫자는 도구 결과에 있는 값만 쓴다. 추측해서 만들지 않는다.
            - 대상 이름은 번호와 한국어 통칭을 함께 (예: M45 플레이아데스).
            - 관측지 위치(위도·경도·지명)는 말하지 않는다.

            진행
            - 순서: 대상 → 구도 → 촬영 설정 → 끝난 뒤. 사용자가 다른 칸부터 말하면 따른다.
            - 질문할 때는 먼저 offer_choices로 그 칸과 선택지(2~4개)를 보여 준 다음 질문한다. 한 번에 한 가지만 묻는다.
            - 사용자가 고르거나 동의하면 바로 set_* 도구로 칸을 채우고 다음으로 넘어간다. 다시 확인하지 않는다.
            - 대상: 추천을 원하면 suggest_targets, 이름을 말하면 search_targets. 대상을 정하면 언제 30°를 넘고 언제 가장 높은지, 화면을 얼마나 채우는지 한 문장으로 알려 준다.
            - 달이 50% 넘게 밝고 대상과 30° 안쪽이면(moon_separation_degrees) 대상을 정하기 전에 그 사실을 알리고, 듀얼 내로우밴드 필터를 쓰거나 달과 먼 대상을 고를 수 있다고 제안한다. 사용자가 그대로 찍겠다면 따른다.
            - 구도: 가운데에 둘지 묻는다. 회전은 사용자가 말할 때만.
            - 촬영 설정: 필터를 먼저 묻고(필터 없음 / 광해 필터 / 듀얼 내로우밴드), recommend_settings로 추천값을 받아 제안한다. 끝나는 조건 기본값은 '대상이 낮아지거나 새벽이 올 때까지'.
            - 끝난 뒤: 기본값(적도의 파킹, 다크·플랫 찍지 않음, 장비 연결 해제)을 제안하고 확인만 받는다.
            - 네 칸이 모두 차면 "오른쪽 계획을 확인해 주세요"라고 짧게 마무리한다.
            - 사용자가 "(칸) 고치기"라고 하면 그 칸을 다시 묻는다.
            - 이 단계에서는 적도의를 움직이거나 촬영을 시작하지 않는다. 그런 요청은 다음 단계(준비)에서 한다고 말한다.
            - 촬영과 관계없는 이야기는 짧게 답하고 계획으로 돌아온다.

            지금 계획 상태(JSON): {{state}}
            """;
    }
}

/// <summary>화면에 보내는 계획 모양 (칸 상태 + 표시용 값)</summary>
public static class PlanView
{
    public static object From(ShootingPlan p, NightContext n)
    {
        static string? Hm(DateTimeOffset? t) => t?.ToString("HH:mm");
        string State(string field, object? value) => value is not null ? "set" : p.Asking == field ? "asking" : "empty";
        var fov = n.Rig.FieldOfView;
        return new
        {
            asking = p.Asking,
            complete = p.Complete,
            fov = new { width = Math.Round(fov.Width, 2), height = Math.Round(fov.Height, 2) },
            target = new
            {
                state = State("target", p.Target),
                value = p.Target is { } t ? new
                {
                    t.Name, t.CommonName, t.KoreanName, t.Type, t.Constellation,
                    sizeArcmin = t.SizeArcmin is { } s ? Math.Round(s, 1) : (double?)null,
                    usableStart = Hm(t.UsableStart), usableEnd = Hm(t.UsableEnd),
                    highestAt = Hm(t.HighestAt), highestAltitude = Math.Round(t.HighestAltitude),
                    transit = Hm(t.Transit), moonSeparation = Math.Round(t.MoonSeparation),
                } : null,
            },
            framing = new
            {
                state = State("framing", p.Framing),
                value = p.Framing is { } f ? new { f.Placement, rotation = Math.Round(f.RotationDegrees), fillPercent = f.FillPercent } : null,
            },
            settings = new
            {
                state = State("settings", p.Settings),
                value = p.Settings is { } s2 ? new
                {
                    exposure = s2.ExposureSeconds, iso = s2.Iso, s2.Filter, s2.EndCondition,
                    start = Hm(s2.Start), end = Hm(s2.End), frames = s2.EstimatedFrames,
                    s2.ExposureRecommended, s2.IsoRecommended,
                } : null,
            },
            after = new
            {
                state = State("after", p.After),
                value = p.After,
            },
        };
    }
}
