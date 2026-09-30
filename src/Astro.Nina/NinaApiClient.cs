using System.Net.Http.Json;
using System.Text.Json;

namespace Astro.Nina;

/// <summary>
/// NINA Advanced API(v2) 클라이언트.
/// 모든 응답은 { "Response": ..., "Success": bool, "Error": string, "StatusCode": int } 형태다.
/// 대기 시간은 요청마다 다르다: 조회는 짧게, 장비 연결은 길게 (HttpClient.Timeout은 그보다 넉넉히 둔다).
/// </summary>
public sealed class NinaApiClient(HttpClient http)
{
    /// <summary>버전·상태·프로필 같은 짧은 조회. NINA가 꺼져 있으면 빨리 알아야 한다.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>장비 연결. 적도의·카메라는 드라이버를 올리느라 5초를 넘기는 경우가 흔하다.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

    /// <summary>플러그인 버전. NINA가 꺼져 있거나 API가 꺼져 있으면 null.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var response = await GetResponseAsync("version", QueryTimeout, ct);
        return response is { ValueKind: JsonValueKind.String } r ? r.GetString() : response?.ToString();
    }

    /// <summary>지금 쓰는 N.I.N.A. 프로필 전체 (장비 설정 포함). 응답이 없으면 null.</summary>
    public Task<JsonElement?> GetActiveProfileAsync(CancellationToken ct = default) =>
        GetResponseAsync("profile/show?active=true", QueryTimeout, ct);

    /// <summary>장비 연결. kind는 camera, mount, focuser, switch, guider 등 (API 경로 이름).</summary>
    public async Task<bool> ConnectAsync(string kind, string deviceId, CancellationToken ct = default) =>
        await GetResponseAsync($"equipment/{kind}/connect?to={Uri.EscapeDataString(deviceId)}", ConnectTimeout, ct) is not null;

    /// <summary>이 PC에 설치되어 N.I.N.A.가 쓸 수 있는 드라이버 목록 (Id, 이름). "없음" 항목 포함.</summary>
    public async Task<IReadOnlyList<(string Id, string Name)>> ListDevicesAsync(string kind, CancellationToken ct = default)
    {
        if (await GetResponseAsync($"equipment/{kind}/list-devices", ConnectTimeout, ct) is not { ValueKind: JsonValueKind.Array } list) return [];
        return list.EnumerateArray()
            .Select(d => (Id: d.TryGetProperty("Id", out var id) ? id.GetString() : null, Name: d.TryGetProperty("Name", out var n) ? n.GetString() : null))
            .Where(d => d.Id is not null)
            .Select(d => (d.Id!, d.Name ?? d.Id!))
            .ToList();
    }

    public async Task<bool> DisconnectAsync(string kind, CancellationToken ct = default) =>
        await GetResponseAsync($"equipment/{kind}/disconnect", ConnectTimeout, ct) is not null;

    /// <summary>프로필 값 바꾸기. settingpath 형식(예: "FilterWheelSettings-Id")은 실기 미검증.</summary>
    public async Task<bool> ChangeProfileValueAsync(string settingPath, string value, CancellationToken ct = default) =>
        await GetResponseAsync($"profile/change-value?settingpath={Uri.EscapeDataString(settingPath)}&newValue={Uri.EscapeDataString(value)}", QueryTimeout, ct) is not null;

    /// <summary>장비가 연결되어 있는지. 응답이 없으면 false.</summary>
    public async Task<bool> IsConnectedAsync(string kind, CancellationToken ct = default) =>
        await GetResponseAsync($"equipment/{kind}/info", QueryTimeout, ct) is { ValueKind: JsonValueKind.Object } info
        && info.TryGetProperty("Connected", out var c) && c.ValueKind == JsonValueKind.True;

    private async Task<JsonElement?> GetResponseAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var res = await http.GetAsync(path, cts.Token);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            if (body.TryGetProperty("Success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            return body.TryGetProperty("Response", out var response) ? response.Clone() : null;
        }
        // 대기 시간 초과는 "응답 없음"으로. 부른 쪽이 취소한 것(화면을 떠남 등)은 그대로 알린다
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return null;
        }
    }
}
