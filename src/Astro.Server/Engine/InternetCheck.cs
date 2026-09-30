using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

public sealed class NetworkOptions
{
    /// <summary>인터넷 연결 확인에 쓰는 주소. 계획 화면의 날씨 예보도 이 서버에서 받는다.</summary>
    public string CheckUrl { get; set; } = "https://api.open-meteo.com/";

    /// <summary>[임시] 화면 설계용: 인터넷이 없는 것처럼 보고한다.</summary>
    public bool SimulateOffline { get; set; }
}

/// <summary>
/// 인터넷 연결 확인. 시작·계획 단계는 온라인 필수라 1단계 엔진 켜기에서 확인한다 (DESIGN.md 1장).
/// 응답 코드는 따지지 않는다: 서버까지 닿기만 하면 연결된 것으로 본다.
/// </summary>
public sealed class InternetCheck(HttpClient http, IOptions<NetworkOptions> options)
{
    public async Task<bool> IsOnlineAsync(CancellationToken ct = default)
    {
        if (options.Value.SimulateOffline) return false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, options.Value.CheckUrl);
            using var _ = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
