namespace Astro.Core.Sky;

/// <summary>관측지. 위도·경도는 도(북·동이 +), 고도는 m.</summary>
public readonly record struct Site(double Latitude, double Longitude, double Elevation = 0);

/// <summary>적도 좌표 (도). 적경도 도 단위(0~360).</summary>
public readonly record struct Equatorial(double Ra, double Dec);

/// <summary>
/// 촬영 계획에 필요한 만큼의 천문 계산 (Meeus 저정밀 식).
/// 태양 약 0.01°, 달 약 0.3°, 시각은 분 단위 정확도 — 계획용으로 충분하고, 실제 정렬·추적은 NINA가 한다.
/// 대상 좌표는 J2000 그대로 쓴다 (세차 약 0.35°, 계획에는 영향 없음).
/// </summary>
public static class Astronomy
{
    private const double Deg = Math.PI / 180;

    public static double JulianDate(DateTime utc) =>
        utc.ToUniversalTime().Ticks / (double)TimeSpan.TicksPerDay + 1721425.5;

    /// <summary>지방 항성시 (도)</summary>
    public static double LocalSiderealDegrees(DateTime utc, double longitude)
    {
        var d = JulianDate(utc) - 2451545.0;
        var t = d / 36525;
        var gmst = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t;
        return Normalize(gmst + longitude);
    }

    /// <summary>고도(도). 대기 굴절은 넣지 않는다.</summary>
    public static double Altitude(Equatorial eq, Site site, DateTime utc)
    {
        var h = (LocalSiderealDegrees(utc, site.Longitude) - eq.Ra) * Deg;
        var lat = site.Latitude * Deg;
        var dec = eq.Dec * Deg;
        return Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(h)) / Deg;
    }

    /// <summary>방위각(도, 북=0, 동=90)</summary>
    public static double Azimuth(Equatorial eq, Site site, DateTime utc)
    {
        var h = (LocalSiderealDegrees(utc, site.Longitude) - eq.Ra) * Deg;
        var lat = site.Latitude * Deg;
        var dec = eq.Dec * Deg;
        var az = Math.Atan2(-Math.Sin(h), Math.Tan(dec) * Math.Cos(lat) - Math.Sin(lat) * Math.Cos(h)) / Deg;
        return Normalize(az);
    }

    /// <summary>시간각(도, -180~180). 0이면 자오선 위.</summary>
    public static double HourAngle(Equatorial eq, Site site, DateTime utc)
    {
        var h = Normalize(LocalSiderealDegrees(utc, site.Longitude) - eq.Ra);
        return h > 180 ? h - 360 : h;
    }

    public static Equatorial Sun(DateTime utc)
    {
        var n = JulianDate(utc) - 2451545.0;
        var l = Normalize(280.460 + 0.9856474 * n);
        var g = Normalize(357.528 + 0.9856003 * n) * Deg;
        var lambda = (l + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Deg;
        var eps = (23.439 - 0.0000004 * n) * Deg;
        var ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda)) / Deg;
        var dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda)) / Deg;
        return new Equatorial(Normalize(ra), dec);
    }

    /// <summary>달의 지심 좌표와 지평 시차(도)</summary>
    public static (Equatorial Position, double Parallax) Moon(DateTime utc)
    {
        var t = (JulianDate(utc) - 2451545.0) / 36525;
        double S(double a) => Math.Sin(a * Deg);
        double C(double a) => Math.Cos(a * Deg);
        var lambda = 218.32 + 481267.881 * t
            + 6.29 * S(135.0 + 477198.87 * t) - 1.27 * S(259.3 - 413335.36 * t)
            + 0.66 * S(235.7 + 890534.22 * t) + 0.21 * S(269.9 + 954397.74 * t)
            - 0.19 * S(357.5 + 35999.05 * t) - 0.11 * S(186.5 + 966404.03 * t);
        var beta = 5.13 * S(93.3 + 483202.02 * t) + 0.28 * S(228.2 + 960400.89 * t)
            - 0.28 * S(318.3 + 6003.15 * t) - 0.17 * S(217.6 - 407332.21 * t);
        var parallax = 0.9508 + 0.0518 * C(134.9 + 477198.87 * t) + 0.0095 * C(259.2 - 413335.36 * t)
            + 0.0078 * C(235.7 + 890534.22 * t) + 0.0028 * C(269.9 + 954397.74 * t);

        var eps = (23.439 - 0.0130 * t) * Deg;
        var l = lambda * Deg;
        var b = beta * Deg;
        var ra = Math.Atan2(Math.Sin(l) * Math.Cos(eps) - Math.Tan(b) * Math.Sin(eps), Math.Cos(l)) / Deg;
        var dec = Math.Asin(Math.Sin(b) * Math.Cos(eps) + Math.Cos(b) * Math.Sin(eps) * Math.Sin(l)) / Deg;
        return (new Equatorial(Normalize(ra), dec), parallax);
    }

    /// <summary>관측지에서 본 달의 고도(도). 시차 보정 포함</summary>
    public static double MoonAltitude(Site site, DateTime utc)
    {
        var (pos, parallax) = Moon(utc);
        var alt = Altitude(pos, site, utc);
        return alt - parallax * Math.Cos(alt * Deg);
    }

    /// <summary>달이 빛나는 비율 (0 = 삭, 1 = 망)</summary>
    public static double MoonIllumination(DateTime utc)
    {
        var elongation = Separation(Sun(utc), Moon(utc).Position);
        return (1 - Math.Cos(elongation * Deg)) / 2;
    }

    /// <summary>두 좌표 사이 각거리(도)</summary>
    public static double Separation(Equatorial a, Equatorial b)
    {
        var cos = Math.Sin(a.Dec * Deg) * Math.Sin(b.Dec * Deg)
            + Math.Cos(a.Dec * Deg) * Math.Cos(b.Dec * Deg) * Math.Cos((a.Ra - b.Ra) * Deg);
        return Math.Acos(Math.Clamp(cos, -1, 1)) / Deg;
    }

    private static double Normalize(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
    }
}
