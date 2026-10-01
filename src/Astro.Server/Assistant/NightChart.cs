using Astro.Core.Sky;
using Astro.Server.Sky;

namespace Astro.Server.Assistant;

/// <summary>
/// 화면 "오늘 밤" 그래프 자료: 시간축 하나에 어두운 시간, 달, 구름, 대상 고도, 촬영 시간대.
/// 시각은 모두 시간축 시작부터의 분(min)으로 보낸다 (화면이 시간대를 따로 계산하지 않게).
/// </summary>
public static class NightChart
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(10);

    public static object Build(NightContext n, ShootingPlan plan, DsoCatalog catalog)
    {
        // 축: 18시~6시. 일몰·일출이 그 밖이면 넓힌다
        var sunset = n.Window.Sunset;
        var from = new DateTimeOffset(sunset.Year, sunset.Month, sunset.Day, 18, 0, 0, sunset.Offset);
        if (sunset.AddMinutes(-30) < from) from = new DateTimeOffset(sunset.Year, sunset.Month, sunset.Day, sunset.AddMinutes(-30).Hour, 0, 0, sunset.Offset);
        var to = from.Date.AddDays(1) is var next ? new DateTimeOffset(next.Year, next.Month, next.Day, 6, 0, 0, sunset.Offset) : from;
        var sunrise = n.Window.Sunrise;
        if (sunrise.AddMinutes(30) > to) to = new DateTimeOffset(sunrise.Year, sunrise.Month, sunrise.Day, sunrise.AddMinutes(90).Hour, 0, 0, sunrise.Offset);

        double Min(DateTimeOffset t) => Math.Round((t - from).TotalMinutes);

        object? target = null;
        if (plan.Target is { } pt && catalog.Get(pt.Id) is { } dso)
        {
            target = new
            {
                name = pt.Name,
                track = Night.Track(utc => Astronomy.Altitude(dso.Position, n.Site, utc), from, to, Step)
                    .Select(x => new[] { Min(x.Time), Math.Round(x.Altitude, 1) }).ToList(),
                transit = pt.Transit is { } tr ? Min(tr) : (double?)null,
                transitLabel = pt.Transit?.ToString("HH:mm"),
            };
        }

        return new
        {
            start = from.ToString("HH:mm"),
            minutes = Min(to),
            hours = Enumerable.Range(0, (int)((to - from).TotalHours) + 1).Select(h => new { at = h * 60, label = from.AddHours(h).ToString("HH") }).ToList(),
            dark = new { start = Min(n.Window.DarkStart), end = Min(n.Window.DarkEnd), label = $"{n.Window.DarkStart:HH:mm}–{n.Window.DarkEnd:HH:mm}" },
            sun = new { set = Min(n.Window.Sunset), rise = Min(n.Window.Sunrise) },
            moon = new
            {
                illumination = Math.Round(n.Moon.Illumination * 100),
                up = n.Moon.UpIntervals.Select(u => new { start = Min(u.Start), end = Min(u.End), setLabel = u.End.ToString("HH:mm"), riseLabel = u.Start.ToString("HH:mm") }).ToList(),
            },
            clouds = n.Clouds.Where(c => c.Time >= from && c.Time < to).Select(c => new { at = Min(c.Time), cover = c.Cover }).ToList(),
            target,
            shooting = plan.Settings is { EstimatedFrames: > 0 } s ? new { start = Min(s.Start), end = Min(s.End) } : null, // 찍을 시간이 없으면 띠 없음
        };
    }
}
