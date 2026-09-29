using System.Net.Http.Json;
using System.Text.Json;

namespace Astro.Nina;

/// <summary>
/// NINA Advanced API(v2) 클라이언트.
/// 모든 응답은 { "Response": ..., "Success": bool, "Error": string, "StatusCode": int } 형태다.
/// </summary>
public sealed class NinaApiClient(HttpClient http)
{
    /// <summary>플러그인 버전. NINA가 꺼져 있거나 API가 꺼져 있으면 null.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var response = await GetResponseAsync("version", ct);
        return response is { ValueKind: JsonValueKind.String } r ? r.GetString() : response?.ToString();
    }

    private async Task<JsonElement?> GetResponseAsync(string path, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (body.TryGetProperty("Success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            return body.TryGetProperty("Response", out var response) ? response.Clone() : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
