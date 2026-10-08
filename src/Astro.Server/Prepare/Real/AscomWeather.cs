namespace Astro.Server.Prepare.Real;

/// <summary>
/// 기온·습도·이슬점 (이슬 여유 = 기온 − 이슬점). WandererBox Environment Sensor(ASCOM ObservingConditions)를 직접 읽는다.
/// 2026-10-08 실기(WandererBox Plus V3 + DHT22): N.I.N.A.가 WandererBox 스위치를 연결한 채로도 동시에 읽힘 — 기온 25.2°C · 습도 36.5% · 이슬점 9.26°C(드라이버가 계산).
/// N.I.N.A. 날씨 장비(weather/info)로 받지 않는 이유: 사용자 프로필에 날씨 장비가 없고, 연결하면 프로필 설정이 바뀐다.
/// 렌즈에 붙인 온도 프로브 값은 이 드라이버에 없다(허브 상태 줄에만 — COM 포트는 Empire가 잡고 있음).
/// 읽기만 한다. 한 번 연결한 드라이버를 계속 쓰고, 값은 1분에 한 번만 새로 읽는다
/// </summary>
public sealed class AscomWeather(ILogger<AscomWeather> log)
{
    public string DriverId { get; set; } = "ASCOM.WandererBoxEnvironment.ObservingConditions";

    public sealed record Reading(double TemperatureC, double HumidityPercent, double DewPointC, DateTimeOffset At)
    {
        public double MarginC => TemperatureC - DewPointC;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private dynamic? _driver;
    private Reading? _last;
    private DateTimeOffset _failedAt = DateTimeOffset.MinValue;
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(1);

    /// <summary>최근 값 (1분 안이면 그대로). 드라이버가 없거나 못 읽으면 null — 실패하면 5분 동안 다시 시도하지 않는다</summary>
    public async Task<Reading?> ReadAsync(CancellationToken ct)
    {
        if (_last is { } l && DateTimeOffset.Now - l.At < Refresh) return l;
        if (DateTimeOffset.Now - _failedAt < TimeSpan.FromMinutes(5)) return _last;
        await _gate.WaitAsync(ct);
        try
        {
            if (_last is { } again && DateTimeOffset.Now - again.At < Refresh) return again;
            return _last = await Task.Run(Read, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "기온·습도를 읽지 못함 ({Driver})", DriverId);
            _failedAt = DateTimeOffset.Now;
            _driver = null;
            return null;
        }
        finally { _gate.Release(); }
    }

    private Reading Read()
    {
        if (_driver is null)
        {
            var type = Type.GetTypeFromProgID(DriverId) ?? throw new InvalidOperationException($"ASCOM 드라이버 {DriverId}를 찾지 못했습니다");
            _driver = Activator.CreateInstance(type) ?? throw new InvalidOperationException("ASCOM 드라이버를 만들지 못했습니다");
        }
        if (!(bool)_driver.Connected) _driver.Connected = true;
        double t = _driver.Temperature, h = _driver.Humidity, d = _driver.DewPoint;
        if (!double.IsFinite(t) || !double.IsFinite(h) || !double.IsFinite(d) || h <= 0) throw new InvalidOperationException($"센서 값이 이상합니다 ({t}, {h}, {d})");
        return new Reading(t, h, d, DateTimeOffset.Now);
    }
}
