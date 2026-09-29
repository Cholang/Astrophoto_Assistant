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

    /// <summary>지금 쓰는 N.I.N.A. 프로필 전체 (장비 설정 포함). 응답이 없으면 null.</summary>
    public Task<JsonElement?> GetActiveProfileAsync(CancellationToken ct = default) =>
        GetResponseAsync("profile/show?active=true", ct);

    /// <summary>장비 연결. kind는 camera, mount, focuser, switch, guider 등 (API 경로 이름).</summary>
    public async Task<bool> ConnectAsync(string kind, string deviceId, CancellationToken ct = default) =>
        await GetResponseAsync($"equipment/{kind}/connect?to={Uri.EscapeDataString(deviceId)}", ct) is not null;

    /// <summary>장비가 연결되어 있는지. 응답이 없으면 false.</summary>
    public async Task<bool> IsConnectedAsync(string kind, CancellationToken ct = default) =>
        await GetResponseAsync($"equipment/{kind}/info", ct) is { ValueKind: JsonValueKind.Object } info
        && info.TryGetProperty("Connected", out var c) && c.ValueKind == JsonValueKind.True;

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
