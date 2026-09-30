namespace Astro.Core.Sky;

/// <summary>오늘 밤 시간 구간. 시각은 모두 관측지의 현지 시각(DateTimeOffset).</summary>
public sealed record NightWindow(
    DateTimeOffset Sunset,
    DateTimeOffset DarkStart,
    DateTimeOffset DarkEnd,
    DateTimeOffset Sunrise,
    /// <summary>천문 박명(-18°)까지 어두워지지 않는 밤이면 항해 박명(-12°)을 쓴다</summary>
    bool AstronomicalDark);

public sealed record MoonNight(double Illumination, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> UpIntervals);

/// <summary>대상 하나의 오늘 밤 모습</summary>
public sealed record TargetNight(
    /// <summary>어두운 시간 안에서 기준 고도 이상인 구간 (없으면 null)</summary>
    DateTimeOffset? UsableStart,
    DateTimeOffset? UsableEnd,
    /// <summary>어두운 시간 안에서 가장 높을 때</summary>
    DateTimeOffset HighestAt,
    double HighestAltitude,
    /// <summary>자오선 통과 (어두운 시간 밖이면 null)</summary>
    DateTimeOffset? Transit,
    /// <summary>밤 가운데쯤 달과의 각거리(도)</summary>
    double MoonSeparation);

/// <summary>한 밤의 시간축 계산. 저녁 날짜(현지) 기준으로 그날 낮 12시 ~ 다음 날 낮 12시를 본다.</summary>
public static class Night
{
    public const double DarkSunAltitude = -18;
    public const double NauticalSunAltitude = -12;
    /// <summary>일몰·일출: 태양 윗가장자리 + 대기 굴절</summary>
    public const double SunsetAltitude = -0.833;
    /// <summary>촬영에 쓸 만한 대상 고도 기준 (대기 두께·빛 공해)</summary>
    public const double UsableAltitude = 30;

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(2);

    public static NightWindow Window(Site site, DateOnly evening, TimeZoneInfo zone)
    {
        var (from, to) = Span(evening, zone);
        double Sun(DateTimeOffset t) => Astronomy.Altitude(Astronomy.Sun(t.UtcDateTime), site, t.UtcDateTime);

        var sunset = Crossing(from, to, Sun, SunsetAltitude, falling: true) ?? from;
        var sunrise = Crossing(sunset, to, Sun, SunsetAltitude, falling: false) ?? to;
        var darkStart = Crossing(sunset, to, Sun, DarkSunAltitude, falling: true);
        var darkEnd = darkStart is { } ds ? Crossing(ds, to, Sun, DarkSunAltitude, falling: false) : null;
        if (darkStart is not null && darkEnd is not null)
            return new NightWindow(sunset, darkStart.Value, darkEnd.Value, sunrise, true);

        // 여름 고위도: 천문 박명까지 안 어두워지면 항해 박명
        var ns = Crossing(sunset, to, Sun, NauticalSunAltitude, falling: true) ?? sunset;
        var ne = Crossing(ns, to, Sun, NauticalSunAltitude, falling: false) ?? sunrise;
        return new NightWindow(sunset, ns, ne, sunrise, false);
    }

    public static MoonNight Moon(Site site, NightWindow night)
    {
        var up = new List<(DateTimeOffset, DateTimeOffset)>();
        DateTimeOffset? start = null;
        for (var t = night.Sunset; t <= night.Sunrise; t += Step)
        {
            var isUp = Astronomy.MoonAltitude(site, t.UtcDateTime) > 0;
            if (isUp && start is null) start = t;
            if (!isUp && start is not null) { up.Add((start.Value, t)); start = null; }
        }
        if (start is not null) up.Add((start.Value, night.Sunrise));
        var middle = night.DarkStart + (night.DarkEnd - night.DarkStart) / 2;
        return new MoonNight(Astronomy.MoonIllumination(middle.UtcDateTime), up);
    }

    public static TargetNight Target(Equatorial target, Site site, NightWindow night, double minAltitude = UsableAltitude)
    {
        DateTimeOffset? usableStart = null, usableEnd = null, transit = null;
        var highestAt = night.DarkStart;
        var highest = double.MinValue;
        double? lastHa = null;
        for (var t = night.DarkStart; t <= night.DarkEnd; t += Step)
        {
            var utc = t.UtcDateTime;
            var alt = Astronomy.Altitude(target, site, utc);
            if (alt > highest) { highest = alt; highestAt = t; }
            if (alt >= minAltitude)
            {
                usableStart ??= t;
                usableEnd = t;
            }
            var ha = Astronomy.HourAngle(target, site, utc);
            if (lastHa is < 0 && ha >= 0) transit = t;
            lastHa = ha;
        }
        var middle = night.DarkStart + (night.DarkEnd - night.DarkStart) / 2;
        var separation = Astronomy.Separation(target, Astronomy.Moon(middle.UtcDateTime).Position);
        return new TargetNight(usableStart, usableEnd, highestAt, highest, transit, separation);
    }

    /// <summary>그래프용: from~to 사이를 step 간격으로 고도를 잰다</summary>
    public static IEnumerable<(DateTimeOffset Time, double Altitude)> Track(
        Func<DateTime, double> altitude, DateTimeOffset from, DateTimeOffset to, TimeSpan step)
    {
        for (var t = from; t <= to; t += step) yield return (t, altitude(t.UtcDateTime));
    }

    private static (DateTimeOffset From, DateTimeOffset To) Span(DateOnly evening, TimeZoneInfo zone)
    {
        var noon = evening.ToDateTime(new TimeOnly(12, 0));
        var from = new DateTimeOffset(noon, zone.GetUtcOffset(noon));
        var next = noon.AddDays(1);
        return (from, new DateTimeOffset(next, zone.GetUtcOffset(next)));
    }

    /// <summary>값이 level을 지나는 첫 시각 (선형 보간)</summary>
    private static DateTimeOffset? Crossing(DateTimeOffset from, DateTimeOffset to, Func<DateTimeOffset, double> f, double level, bool falling)
    {
        var prevT = from;
        var prev = f(from);
        for (var t = from + Step; t <= to; t += Step)
        {
            var v = f(t);
            var crossed = falling ? prev >= level && v < level : prev < level && v >= level;
            if (crossed)
            {
                var k = (level - prev) / (v - prev);
                return prevT + (t - prevT) * k;
            }
            prevT = t;
            prev = v;
        }
        return null;
    }
}
