namespace Astro.Server.Shoot;

/// <summary>사진 한 장의 측정값. Eccentricity·Mean은 못 재면 null</summary>
public sealed record FrameStats(double Hfr, int Stars, double? Mean, double? Eccentricity, double? GuideRmsArcsec);

/// <summary>그날 밤 기준: 초점 맞출 때 별 크기, 처음 좋은 사진들의 별 수·배경, 주 카메라 한 픽셀(″)</summary>
public sealed record GradeBaseline(double FocusHfr, int? Stars, double? Mean, double PixelScaleArcsec);

public sealed record Grade(string Letter, string Reason)
{
    public bool Excluded => Letter == "F";
}

/// <summary>
/// 사진 등급 (DESIGN.md "촬영" — 2026-10-07 사용자 결정: A+ · A · B · C · F). 판정은 규칙, AI는 쓰지 않는다.
/// 기준은 그날 밤 자기 사진 — 초점 때 별 크기와 처음 좋은 사진들. 기준값(배수)은 실제 장비 사진으로 다듬는다.
/// F(실패)는 제외 폴더로 옮긴다: 별이 흐름(가이딩 오차 큼·별이 길쭉함), 구름(별 수 급감), 잡광(배경이 크게 밝아짐), 초점이 크게 나감.
/// </summary>
public static class ShotGrader
{
    public static readonly string[] Letters = ["A+", "A", "B", "C", "F"];

    /// <summary>등급별 분류 기준 (화면 툴팁)</summary>
    public static readonly IReadOnlyDictionary<string, string> Criteria = new Dictionary<string, string>
    {
        ["A+"] = "A+ · 가장 좋음: 별 크기가 초점 맞출 때와 거의 같고(5% 안), 별이 둥글고, 가이딩 오차가 작아요.",
        ["A"] = "A · 좋음: 별 크기가 초점 때보다 15% 안으로 커졌고 별이 둥글어요.",
        ["B"] = "B · 괜찮음: 별 크기가 초점 때보다 30% 안으로 커졌거나 별이 조금 길쭉해요. 스태킹에 그대로 써요.",
        ["C"] = "C · 아쉬움: 별 크기가 초점 때보다 30% 넘게 커졌거나 배경이 밝아요. 쓸 수는 있지만 결과를 조금 떨어뜨릴 수 있어요.",
        ["F"] = "F · 실패: 별이 흘렀거나(바람·케이블 걸림), 구름으로 별이 크게 줄었거나, 잡광이 들어왔어요. 이 사진은 제외 폴더로 따로 옮겨요.",
    };

    public const double TrailGuideFactor = 2.5;  // 가이딩 오차가 한 픽셀의 2.5배 넘으면 별이 흐름
    public const double TrailEccentricity = 0.75;
    public const double CloudStarRatio = 0.4;    // 별 수가 기준의 40% 아래면 구름
    public const double LightMeanFactor = 2.5;   // 배경이 기준의 2.5배 넘게 밝으면 잡광
    public const double DefocusFactor = 1.8;     // 별 크기가 초점 때의 1.8배 넘으면 실패

    public static Grade Judge(FrameStats s, GradeBaseline b)
    {
        if (s.GuideRmsArcsec is { } g && g > b.PixelScaleArcsec * TrailGuideFactor) return new("F", "별이 흘렀어요 (가이딩 오차가 커요)");
        if (s.Eccentricity is { } e && e > TrailEccentricity) return new("F", "별이 한쪽으로 흘렀어요");
        if (b.Stars is { } bs && bs > 0 && s.Stars < bs * CloudStarRatio) return new("F", "별이 크게 줄었어요 (구름)");
        if (b.Mean is { } bm && bm > 0 && s.Mean is { } m && m > bm * LightMeanFactor) return new("F", "배경이 크게 밝아졌어요 (잡광)");
        if (s.Hfr <= 0 || s.Stars == 0) return new("F", "별을 찾지 못했어요");
        var r = s.Hfr / b.FocusHfr;
        if (r > DefocusFactor) return new("F", "별이 크게 번졌어요 (초점)");
        var bright = b.Mean is { } bm2 && bm2 > 0 && s.Mean is { } m2 && m2 > bm2 * 1.5;
        var oval = s.Eccentricity is > 0.6;
        var steady = s.GuideRmsArcsec is not { } gr || gr <= b.PixelScaleArcsec * 0.5;
        if (r > 1.3 || bright) return new("C", bright ? "배경이 밝아요" : "별이 커졌어요");
        if (r > 1.15 || oval) return new("B", oval ? "별이 조금 길쭉해요" : "별이 조금 커졌어요");
        if (r > 1.05 || !steady) return new("A", "좋아요");
        return new("A+", "아주 좋아요");
    }
}
