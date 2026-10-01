using System.Text.Json;
using Astro.Core.Sky;
using Astro.Nina;

namespace Astro.Server.Sky;

/// <summary>촬영 장비 요약 (N.I.N.A. 프로필에서). 화각 계산에 쓴다.</summary>
public sealed record Rig(
    string Telescope, double FocalLength, double FocalRatio,
    string Camera, double PixelSize, int SensorWidth, int SensorHeight,
    bool HasFilterWheel, bool HasGuider)
{
    /// <summary>화각(도): 가로·세로</summary>
    public (double Width, double Height) FieldOfView =>
        (Fov(SensorWidth), Fov(SensorHeight));

    /// <summary>픽셀 스케일("/px)</summary>
    public double PixelScale => 206.265 * PixelSize / FocalLength;

    private double Fov(int px) => 2 * Math.Atan(px * PixelSize / 1000 / 2 / FocalLength) * 180 / Math.PI;
}

/// <summary>시간별 구름양(%)</summary>
public sealed record CloudHour(DateTimeOffset Time, int Cover);

/// <summary>
/// 오늘 밤 정보를 모은다: 관측지·장비(N.I.N.A. 프로필), 어두운 시간·달(계산), 구름(Open-Meteo 예보).
/// 관측지 좌표는 화면·AI에 넘기지 않는다 (계산에만 쓴다).
/// </summary>
public sealed class TonightService(NinaApiClient nina, IHttpClientFactory httpFactory, ILogger<TonightService> log, Engine.OpticsStore optics)
{
    private (DateTimeOffset At, Site Site, IReadOnlyList<CloudHour> Clouds)? _cloudCache;

    public async Task<(Site Site, Rig Rig)?> ReadProfileAsync(CancellationToken ct)
    {
        if (await nina.GetActiveProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } p) return null;
        double Num(string section, string key) =>
            p.TryGetProperty(section, out var s) && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        string Str(string section, string key) =>
            p.TryGetProperty(section, out var s) && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var site = new Site(Num("AstrometrySettings", "Latitude"), Num("AstrometrySettings", "Longitude"), Num("AstrometrySettings", "Elevation"));
        var camera = Str("CameraSettings", "LastDeviceName");
        var paren = camera.IndexOf(" (", StringComparison.Ordinal);
        // 망원경은 AA의 망원경 목록이 기준 (N.I.N.A.에도 써 넣지만, 아직 안 썼을 수 있으니 여기서 직접)
        var scope = optics.Current;
        var rig = new Rig(
            scope?.Name ?? Str("TelescopeSettings", "Name"),
            scope?.EffectiveFocalLength ?? Num("TelescopeSettings", "FocalLength"),
            scope?.EffectiveFocalRatio ?? Num("TelescopeSettings", "FocalRatio"),
            paren > 0 ? camera[..paren] : camera,
            Num("CameraSettings", "PixelSize"),
            (int)Num("FramingAssistantSettings", "CameraWidth"),
            (int)Num("FramingAssistantSettings", "CameraHeight"),
            Str("FilterWheelSettings", "Id") is { Length: > 0 } fw && fw != "No_Device",
            Str("GuiderSettings", "GuiderName") is { Length: > 0 } g && g != "No_Guider");
        return (site, rig);
    }

    /// <summary>계획할 밤의 저녁 날짜. 새벽(8시 전)이면 지금 진행 중인 밤(어제 저녁), 그 뒤로는 오늘 저녁</summary>
    public static DateOnly EveningOf(DateTimeOffset now) =>
        DateOnly.FromDateTime(now.Hour < 8 ? now.DateTime.AddDays(-1) : now.DateTime);

    /// <summary>시간별 구름양 예보 (한 시간 동안 저장). 인터넷이 안 되면 빈 목록</summary>
    public async Task<IReadOnlyList<CloudHour>> CloudsAsync(Site site, CancellationToken ct)
    {
        if (_cloudCache is { } c && c.Site == site && DateTimeOffset.Now - c.At < TimeSpan.FromHours(1)) return c.Clouds;
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={site.Latitude:F3}&longitude={site.Longitude:F3}&hourly=cloud_cover&timezone=UTC&forecast_days=3");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            var hourly = doc.RootElement.GetProperty("hourly");
            var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.SpecifyKind(DateTime.Parse(t.GetString()!), DateTimeKind.Utc)).ToList();
            var covers = hourly.GetProperty("cloud_cover").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1).ToList();
            var list = times.Zip(covers).Where(x => x.Second >= 0)
                .Select(x => new CloudHour(new DateTimeOffset(x.First).ToLocalTime(), x.Second)).ToList();
            _cloudCache = (DateTimeOffset.Now, site, list);
            return list;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            log.LogWarning("구름 예보를 받지 못했습니다: {Message}", e.Message);
            return _cloudCache?.Clouds ?? [];
        }
    }
}
