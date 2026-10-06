namespace Astro.Server.Shoot;

/// <summary>가이드 별을 잃었거나 약해진 원인 (docs/SHOOT_IMPLEMENTATION.md "B. 가이딩 중 가이드 별을 잃었을 때")</summary>
public enum GuideLoss
{
    None,
    /// <summary>차 불빛·손전등: 갑자기, 주 사진 배경이 크게 밝아짐</summary>
    Light,
    /// <summary>구름: 별 신호가 줄어 잃음, 주 사진 별 수도 줄어듦</summary>
    Cloud,
    /// <summary>가이드 망원경 이슬: 가이딩 중 별 신호가 천천히 약해지고 기온이 이슬점 가까이</summary>
    Dew,
    /// <summary>기온 변화로 가이드 초점 밀림: 별 크기(HFD)가 커지며 약해짐</summary>
    GuideFocus,
    /// <summary>가이딩 중 별 신호가 약해짐 (원인을 가리지 못함) — 가이드 노출을 올려 버팀</summary>
    Faint,
    /// <summary>바람·케이블 걸림: 별이 한두 걸음 만에 크게 튐</summary>
    Jump,
    /// <summary>적도의가 멈춤 (한계·전원): 추적 꺼짐</summary>
    MountStopped,
    /// <summary>가이드 카메라·PHD2 연결 끊김</summary>
    Disconnected,
}

/// <summary>
/// 장비에서 읽은 가이딩 신호 (최근 것이 끝). Snr·Hfd는 PHD2 GuideStep, LastJumpPx는 최근 몇 걸음 중 가장 큰 오차(픽셀),
/// DewMarginC = 기온 − 이슬점(모르면 null)
/// </summary>
public sealed record GuideRaw(bool Connected, bool Guiding, bool MountTracking, IReadOnlyList<double> Snr, IReadOnlyList<double> Hfd, double? LastJumpPx, double? DewMarginC);

/// <summary>원인 판정에 쓰는 것: 장비 신호 + 주 사진(최근 사진의 별 수·배경 ÷ 그날 기준) + 가이딩 기준(처음 안정됐을 때의 SNR·HFD)</summary>
public sealed record GuideSignals(GuideRaw Raw, double? MainStarsRatio, double? MainMeanRatio, double SnrBaseline, double HfdBaseline);

/// <summary>원인 가르기 (순수 함수 — 판정은 규칙, 기준값은 첫 출사에서 다듬는다)</summary>
public static class GuideWatch
{
    /// <summary>가이드 별을 잃은 원인인가 (약해짐·없음이 아닌 것)</summary>
    public static bool IsLost(this GuideLoss l) => l is GuideLoss.Light or GuideLoss.Cloud or GuideLoss.Jump or GuideLoss.MountStopped or GuideLoss.Disconnected;

    public const double JumpPx = 4;          // 한 걸음에 4픽셀 넘게 튀면 바람·케이블
    public const double LightMean = 1.8;     // 주 사진 배경이 기준의 1.8배 넘게 밝으면 빛
    public const double FaintRatio = 0.5;    // 최근 SNR이 기준의 절반 아래면 약해짐
    public const double DewMarginC = 2;      // 기온 − 이슬점 2°C 아래면 이슬 위험
    public const double HfdGrowth = 1.3;
    public const int Recent = 10;            // 최근 걸음 수 (가이드 노출 1~2초 → 10~20초)

    public static GuideLoss Classify(GuideSignals s)
    {
        var r = s.Raw;
        if (!r.Connected) return GuideLoss.Disconnected;
        if (!r.MountTracking) return GuideLoss.MountStopped;
        if (!r.Guiding)
        {
            if (r.LastJumpPx > JumpPx) return GuideLoss.Jump;
            if (s.MainMeanRatio > LightMean) return GuideLoss.Light;
            // 갑자기 잃음(잃기 직전까지 신호가 기준 근처) + 주 사진 별 수는 그대로면 빛, 아니면 구름.
            // 그날 기준이 아직 없으면 바로 앞 신호들의 최댓값과 비교
            var reference = s.SnrBaseline > 0 ? s.SnrBaseline : r.Snr.Count >= 2 ? r.Snr.SkipLast(1).TakeLast(Recent).Max() : 0;
            var sudden = r.Snr.Count >= 2 && reference > 0 && r.Snr[^1] > reference * 0.6;
            if (sudden && s.MainStarsRatio is null or > 0.6) return GuideLoss.Light;
            return GuideLoss.Cloud;
        }
        if (r.Snr.Count < Recent || s.SnrBaseline <= 0) return GuideLoss.None;
        var snrNow = r.Snr.TakeLast(Recent).Average();
        if (snrNow >= s.SnrBaseline * FaintRatio) return GuideLoss.None;
        if (r.DewMarginC is < DewMarginC) return GuideLoss.Dew;
        if (r.Hfd.Count >= Recent && s.HfdBaseline > 0 && r.Hfd.TakeLast(Recent).Average() > s.HfdBaseline * HfdGrowth) return GuideLoss.GuideFocus;
        if (s.MainStarsRatio is < 0.6) return GuideLoss.Cloud; // 옅은 구름: 가이딩은 되지만 별이 줄어듦
        return GuideLoss.Faint;
    }

    /// <summary>이슬점 (Magnus 식, °C)</summary>
    public static double DewPoint(double tempC, double humidityPercent)
    {
        const double a = 17.62, b = 243.12;
        var g = Math.Log(Math.Max(1, humidityPercent) / 100) + a * tempC / (b + tempC);
        return b * g / (a - g);
    }

    /// <summary>화면 제목·멈춘 시간 기록에 쓰는 이름</summary>
    public static string Key(GuideLoss l) => l switch
    {
        GuideLoss.Light => "light",
        GuideLoss.Cloud => "cloud",
        GuideLoss.Dew => "dew",
        GuideLoss.GuideFocus => "guidefocus",
        GuideLoss.Faint => "faint",
        GuideLoss.Jump => "jump",
        GuideLoss.MountStopped => "mount",
        GuideLoss.Disconnected => "guider",
        _ => "",
    };

    public static string Label(string key) => key switch
    {
        "light" => "강한 빛",
        "cloud" => "구름",
        "dew" => "이슬",
        "guidefocus" => "가이드 초점",
        "jump" => "바람·케이블",
        "mount" => "적도의 멈춤",
        "guider" => "장비 끊김",
        _ => "기타",
    };
}
