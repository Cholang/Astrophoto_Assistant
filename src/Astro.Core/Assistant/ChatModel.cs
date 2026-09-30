using System.Text.Json;

namespace Astro.Core.Assistant;

// AI 회사와 무관한 대화 형식. 회사별 차이는 IChatModel 구현(번역기) 안에서만 다룬다.
// 새 회사를 붙일 때는 IChatModel 하나만 만들면 되고, 도구·프롬프트·계획 상태는 그대로 쓴다.

public enum ChatRole { User, Assistant }

/// <summary>
/// 메시지의 한 조각: 글, 도구 호출, 도구 결과 중 하나.
/// ProviderState: 회사가 "다음 요청에 그대로 돌려 달라"고 준 값(예: Gemini의 thoughtSignature). 다른 곳에서는 건드리지 않는다.
/// </summary>
public abstract record ChatPart(string? ProviderState = null);

public sealed record TextPart(string Text, string? ProviderState = null) : ChatPart(ProviderState);

/// <summary>AI가 도구를 부름. Arguments는 JSON 객체</summary>
public sealed record ToolCallPart(string Id, string Name, JsonElement Arguments, string? ProviderState = null) : ChatPart(ProviderState);

/// <summary>도구를 실행한 결과. Result는 JSON 객체</summary>
public sealed record ToolResultPart(string CallId, string Name, JsonElement Result) : ChatPart();

public sealed record ChatMessage(ChatRole Role, IReadOnlyList<ChatPart> Parts)
{
    public static ChatMessage User(string text) => new(ChatRole.User, [new TextPart(text)]);
}

/// <summary>AI에게 쥐여 주는 도구. Parameters는 JSON 스키마(type: object)</summary>
public sealed record ToolSpec(string Name, string Description, JsonElement Parameters);

/// <summary>응답이 오는 동안의 조각들</summary>
public abstract record ChatChunk;

/// <summary>화면에 바로 흘려 보낼 글</summary>
public sealed record TextDelta(string Text) : ChatChunk;

/// <summary>응답 한 번이 끝남. Message는 대화 기록에 그대로 붙인다 (도구 호출·ProviderState 포함)</summary>
public sealed record ChatCompleted(ChatMessage Message) : ChatChunk;

public interface IChatModel
{
    /// <summary>표시용 이름 (예: "Gemini 3.5 Flash")</summary>
    string Name { get; }

    IAsyncEnumerable<ChatChunk> StreamAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken ct = default);
}

public enum ChatFailure
{
    /// <summary>연결·응답 문제. 다시 보내면 될 수 있다</summary>
    Other,
    /// <summary>서버 과부하. 잠시 뒤 다시</summary>
    Busy,
    /// <summary>사용 한도 초과 (무료 한도 등)</summary>
    Quota,
    /// <summary>키가 없거나 틀림</summary>
    Key,
}

/// <summary>AI를 부르지 못함. Message는 화면에 보여 줄 문장, Kind로 연습 대화로 넘어갈지 정한다</summary>
public sealed class ChatModelException(string message, ChatFailure kind = ChatFailure.Other, Exception? inner = null) : Exception(message, inner)
{
    public ChatFailure Kind { get; } = kind;
}
