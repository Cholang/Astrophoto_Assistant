using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Astro.Core.Setup;
using Astro.Nina;
using Astro.Server.Assistant;
using Astro.Server.Engine;
using Astro.Server.Setup;
using Microsoft.Extensions.Options;

namespace Astro.Server.Sky;

/// <summary>지금 N.I.N.A.에 설정된 관측지. 위도·경도가 모두 0이면 "없음"(null)으로 본다</summary>
public sealed record CurrentSite(double Latitude, double Longitude, double Elevation);

/// <summary>
/// 관측지 (DESIGN.md 3장 "관측지"): 관측지의 기준은 AA(프로필의 관측지 목록)이고, N.I.N.A.는 AA 값을 따라간다.
/// 저장하면 N.I.N.A. 프로필의 위도·경도·고도를 바꾸고, 연결된 적도의에도 새 위치를 보낸다.
/// </summary>
public sealed class SiteService(NinaApiClient nina, IHttpClientFactory httpFactory, IOptions<EquipmentOptions> equipment, PlanAssistant plan, ILogger<SiteService> log)
{
    private static readonly CheckItem Save = new("nina", "N.I.N.A.에 관측지 저장", null, null);
    private static readonly CheckItem Mount = new("mount", "적도의에 위치 보내기", null, null);

    public async Task<CurrentSite?> CurrentAsync(CancellationToken ct)
    {
        if (await nina.GetActiveProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } profile
            || !profile.TryGetProperty("AstrometrySettings", out var a)) return null;
        double Num(string key) => a.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        var site = new CurrentSite(Num("Latitude"), Num("Longitude"), Num("Elevation"));
        return site is { Latitude: 0, Longitude: 0 } ? null : site;
    }

    /// <summary>좌표의 고도(m). 무료 고도 조회(Open-Meteo). 실패하면 0</summary>
    public async Task<double> ElevationAsync(double latitude, double longitude, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = string.Create(CultureInfo.InvariantCulture, $"https://api.open-meteo.com/v1/elevation?latitude={latitude:F5}&longitude={longitude:F5}");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            return doc.RootElement.GetProperty("elevation")[0].GetDouble();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogWarning(e, "고도 조회 실패");
            return 0;
        }
    }

    /// <summary>
    /// 관측지 적용: N.I.N.A.에 저장 → 적도의에 위치 보내기. 단계마다 한 건씩 (화면의 원형 표시가 문장을 바꾼다).
    /// 적도의는 연결을 끊었다 다시 이어서 N.I.N.A.가 연결할 때 새 위치를 적도의로 보내게 한다
    /// (N.I.N.A. 설정 "적도의 위치 동기화"를 TOTELESCOPE로 맞춘다 — PROMPT면 N.I.N.A. 창이 떠서 멈춘다). 실기 미검증.
    /// 장비 시뮬레이션 중에는 적도의를 건드리지 않고 흉내만 낸다.
    /// </summary>
    public async IAsyncEnumerable<CheckResult> ApplyAsync(double latitude, double longitude, double elevation, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return Save.Running("N.I.N.A.에 관측지를 저장하는 중입니다");
        var inv = CultureInfo.InvariantCulture;
        var saved = await nina.ChangeProfileValueAsync("AstrometrySettings-Latitude", latitude.ToString("R", inv), ct)
            && await nina.ChangeProfileValueAsync("AstrometrySettings-Longitude", longitude.ToString("R", inv), ct)
            && await nina.ChangeProfileValueAsync("AstrometrySettings-Elevation", Math.Round(elevation).ToString(inv), ct);
        if (!saved)
        {
            yield return Save.Fail("N.I.N.A.에 관측지를 저장하지 못했습니다", new Diagnosis([], "N.I.N.A.가 켜져 있는지 확인한 뒤 다시 저장해 주세요."));
            yield break;
        }
        // 위치가 바뀌었으니 계획 화면의 오늘 밤 정보(박명·고도·구름)는 새로 만든다
        plan.Reset();
        yield return Save.Pass("N.I.N.A.에 관측지를 저장했습니다");

        yield return Mount.Running("적도의에 위치를 보내는 중입니다");
        if (equipment.Value.Simulate)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), ct); // 시뮬레이션: 단계가 보이게 잠깐
            yield return Mount.Pass("적도의에 위치를 보냈습니다");
            yield break;
        }
        if (!await nina.IsConnectedAsync("mount", ct))
        {
            // 연결 전이면 다음에 연결할 때 N.I.N.A.가 보낸다
            await nina.ChangeProfileValueAsync("TelescopeSettings-TelescopeLocationSyncDirection", "TOTELESCOPE", ct);
            yield return Mount.Pass("적도의를 연결할 때 위치를 보냅니다");
            yield break;
        }
        await nina.ChangeProfileValueAsync("TelescopeSettings-TelescopeLocationSyncDirection", "TOTELESCOPE", ct);
        var profile = await nina.GetActiveProfileAsync(ct);
        var mountId = profile is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("TelescopeSettings", out var t)
            && t.TryGetProperty("Id", out var id) ? id.GetString() : null;
        var ok = mountId is not null
            && await nina.DisconnectAsync("mount", ct)
            && await nina.ConnectAsync("mount", mountId, ct)
            && await nina.IsConnectedAsync("mount", ct);
        yield return ok
            ? Mount.Pass("적도의에 위치를 보냈습니다")
            : Mount.Fail("적도의에 위치를 보내지 못했습니다", new Diagnosis([], "적도의 연결 상태를 확인한 뒤 관측지를 다시 저장해 주세요. N.I.N.A.에는 새 관측지가 저장되어 있습니다."));
    }
}
