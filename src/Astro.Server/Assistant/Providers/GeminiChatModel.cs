using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astro.Core.Assistant;

namespace Astro.Server.Assistant.Providers;

/// <summary>
/// Google Gemini (generativelanguage REST, SSE 스트리밍).
/// 과부하(503)·한도(429)면 설정의 다음 모델로 넘어간다. 글이 나오기 시작한 뒤에는 넘어가지 않는다.
/// Gemini 3의 thoughtSignature는 ProviderState에 담아 다음 요청에 그대로 돌려준다.
/// </summary>
public sealed class GeminiChatModel(HttpClient http, string apiKey, IReadOnlyList<string> models, string effort = "low") : IChatModel
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    public string Name => $"Gemini ({models.FirstOrDefault()})";

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequest(system, messages, tools, effort);
        HttpResponseMessage? response = null;
        var lastError = "";
        var allQuota = true; // 모든 모델이 한도 초과(429)였는지
        foreach (var model in models)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{model}:streamGenerateContent?alt=sse")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", apiKey);
            try
            {
                response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException e)
            {
                throw new ChatModelException("AI 서버에 연결하지 못했습니다. 인터넷 연결을 확인해 주세요.", ChatFailure.Other, e);
            }
            if (response.IsSuccessStatusCode) break;

            lastError = await ErrorMessage(response, ct);
            var status = response.StatusCode;
            if (status != HttpStatusCode.TooManyRequests) allQuota = false;
            response.Dispose();
            response = null;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || (status == HttpStatusCode.BadRequest && lastError.Contains("API key", StringComparison.OrdinalIgnoreCase)))
                throw new ChatModelException("AI API 키가 올바르지 않습니다. 키를 다시 넣어 주세요.", ChatFailure.Key);
            if (status is not (HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.NotFound))
                throw new ChatModelException($"AI가 요청을 받지 않았습니다. ({(int)status}) {lastError}");
            // 다음 모델로
        }
        if (response is null)
            throw allQuota
                ? new ChatModelException("오늘 AI 사용 한도를 다 썼습니다.", ChatFailure.Quota, new Exception(lastError))
                : new ChatModelException("AI 서버가 지금 바쁩니다. 잠시 뒤 다시 보내 주세요.", ChatFailure.Busy, new Exception(lastError));

        using (response)
        {
            var parts = new List<ChatPart>();
            var text = new StringBuilder();
            string? textSignature = null;

            void FlushText()
            {
                if (text.Length == 0 && textSignature is null) return;
                parts.Add(new TextPart(text.ToString(), textSignature));
                text.Clear();
                textSignature = null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                using var doc = JsonDocument.Parse(line.AsMemory(5));
                if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0) continue;
                if (!cands[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var ps)) continue;

                foreach (var p in ps.EnumerateArray())
                {
                    if (p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True) continue;
                    var signature = p.TryGetProperty("thoughtSignature", out var sig) ? sig.GetString() : null;
                    if (p.TryGetProperty("functionCall", out var fc))
                    {
                        FlushText();
                        var id = fc.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        parts.Add(new ToolCallPart(
                            id ?? Guid.NewGuid().ToString("N"),
                            fc.GetProperty("name").GetString()!,
                            fc.TryGetProperty("args", out var args) ? args.Clone() : JsonDocument.Parse("{}").RootElement.Clone(),
                            signature));
                    }
                    else if (p.TryGetProperty("text", out var t))
                    {
                        var s = t.GetString() ?? "";
                        if (s.Length > 0)
                        {
                            text.Append(s);
                            yield return new TextDelta(s);
                        }
                        if (signature is not null) textSignature = signature;
                    }
                    else if (signature is not null)
                    {
                        textSignature = signature;
                    }
                }
            }
            FlushText();
            yield return new ChatCompleted(new ChatMessage(ChatRole.Assistant, parts));
        }
    }

    private static string BuildRequest(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, string effort)
    {
        var contents = new JsonArray();
        foreach (var m in messages)
        {
            var parts = new JsonArray();
            foreach (var part in m.Parts)
            {
                JsonObject node = part switch
                {
                    TextPart t => new JsonObject { ["text"] = t.Text },
                    ToolCallPart c => new JsonObject
                    {
                        ["functionCall"] = new JsonObject { ["id"] = c.Id, ["name"] = c.Name, ["args"] = JsonNode.Parse(c.Arguments.GetRawText()) },
                    },
                    ToolResultPart r => new JsonObject
                    {
                        ["functionResponse"] = new JsonObject { ["id"] = r.CallId, ["name"] = r.Name, ["response"] = JsonNode.Parse(r.Result.GetRawText()) },
                    },
                    _ => throw new NotSupportedException(part.GetType().Name),
                };
                if (part.ProviderState is { } s) node["thoughtSignature"] = s;
                parts.Add(node);
            }
            contents.Add(new JsonObject { ["role"] = m.Role == ChatRole.User ? "user" : "model", ["parts"] = parts });
        }

        var root = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
            ["contents"] = contents,
            // 공통 effort → Gemini thinkingLevel (low · medium · high)
            ["generationConfig"] = new JsonObject { ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = effort } },
        };
        if (tools.Count > 0)
        {
            var decls = new JsonArray();
            foreach (var t in tools)
                decls.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText()) });
            root["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = decls });
        }
        return root.ToJsonString();
    }

    private static async Task<string> ErrorMessage(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
        }
        catch { return res.ReasonPhrase ?? ""; }
    }
}
