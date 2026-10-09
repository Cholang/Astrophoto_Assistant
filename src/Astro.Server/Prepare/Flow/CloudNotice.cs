using Astro.Server.Sky;

namespace Astro.Server.Prepare.Flow;

/// <summary>
/// 구름 예보 알림 (사전 점검 — 알리고 진행): 극축 정렬·캘리브레이션은 별이 보여야 하므로, 지금 시각의 구름양 예보(계획 화면과 같은 open-meteo)가 많으면
/// 안내 문장에 덧붙인다. 막지는 않는다 — 예보가 틀리거나 구름 사이로 될 수 있다. 예보를 못 받으면 말하지 않는다
/// </summary>
public static class CloudNotice
{
    /// <summary>이 이상(%)이면 알린다</summary>
    public const int Many = 70;

    public static async Task<string?> ReadAsync(PrepContext ctx, CancellationToken ct)
    {
        if (ctx.Clouds is not { } read) return null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        try { return Text(await read(limit.Token), ctx.Now()); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    public static string? Text(IReadOnlyList<CloudHour> hours, DateTimeOffset now) =>
        hours.FirstOrDefault(h => h.Time <= now && now < h.Time.AddHours(1)) is { Cover: >= Many } h
            ? $"지금 이곳 하늘은 구름이 {h.Cover}% 덮을 것으로 예보됩니다. 하늘이 맑아 보이면 그대로 진행해도 되지만, 별이 가려지면 별을 찾지 못해 실패할 수 있어요."
            : null;

    /// <summary>안내 문장 뒤에 구름 알림을 붙인다</summary>
    public static string With(string text, string? cloud) => cloud is null ? text : $"{text} {cloud}";
}
