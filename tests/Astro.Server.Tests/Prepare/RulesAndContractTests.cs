using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Tasks.Calibration;
using Astro.Server.Prepare.Tasks.FocusCheck;
using Astro.Server.Prepare.Tasks.Guiding;
using Astro.Server.Prepare.Tasks.Polar;
using Astro.Server.Prepare.Tasks.TestShot;

namespace Astro.Server.Tests.Prepare;

/// <summary>판정 규칙(순수 함수)과 결과 기록의 모양 (모양이 바뀌면 여러 작업이 영향 — 따로 리뷰)</summary>
public class RulesAndContractTests
{
    [Theory]
    [InlineData(0.6, "완벽해요")]
    [InlineData(3.0, "충분해요")]
    [InlineData(9.0, "아쉬워요")]
    [InlineData(30.0, "멀어요")]
    public void 극축_등급(double arcmin, string grade) => Assert.Equal(grade, PolarRules.Grade(arcmin, 5));

    [Fact]
    public void 극축_허용_오차는_계획에서_계산하고_2에서_10분_사이()
    {
        var t = PolarRules.ToleranceArcmin(Harness.Context());
        Assert.InRange(t, 2, 10);
    }

    [Fact]
    public void 가이딩_판정은_주_카메라_한_픽셀_기준()
    {
        Assert.Equal("충분해요", GuidingRules.Judge(new GuideStats(1.0, 1.0, 0), 2.41).Grade);
        Assert.Equal("괜찮아요, 지켜볼게요", GuidingRules.Judge(new GuideStats(2.0, 2.0, 0), 2.41).Grade);
        Assert.Equal("아쉬워요", GuidingRules.Judge(new GuideStats(3.0, 3.0, 0), 2.41).Grade);
        Assert.Equal("괜찮아요, 지켜볼게요", GuidingRules.Judge(new GuideStats(1.0, 1.0, 2), 2.41).Grade); // 별을 잃으면 충분 아님
    }

    [Fact]
    public void 캘리브레이션_판정()
    {
        Assert.Equal("좋아요", CalibrationRules.Judge(new CalibrationData(true, 1.2, 1, .96, [])).Grade);
        Assert.Equal("다시 하는 게 좋아요", CalibrationRules.Judge(new CalibrationData(true, 15, 1, .96, [])).Grade);
        Assert.Equal("다시 하는 게 좋아요", CalibrationRules.Judge(new CalibrationData(true, 1, 1, .3, [])).Grade);
        Assert.Equal("다시 하는 게 좋아요", CalibrationRules.Judge(new CalibrationData(true, 1, 1, 1, ["Dec 백래시"])).Grade);
    }

    [Fact]
    public void 시험_사진_판정()
    {
        var ok = new ShotStats(2.3, .3, .2, .4, .6, "a");
        Assert.Equal(Tone.Ok, TestShotRules.Judge(ok, 2.1, 120).Tone);
        Assert.Equal(90, TestShotRules.Judge(ok with { Background = .5 }, 2.1, 120).SuggestExposure);
        Assert.Equal("redo:focuscheck", TestShotRules.Judge(ok with { Hfr = 3.0 }, 2.1, 120).Problem!.RedoAction);
        Assert.Equal("redo:guiding", TestShotRules.Judge(ok with { Eccentricity = .8 }, 2.1, 120).Problem!.RedoAction);
    }

    [Fact]
    public void 초점_확인_근거_두_가지()
    {
        // 기온 변화 2°C 이상, 또는 센터링 사진 별 크기가 초점 때보다 30% 이상 (별이 충분할 때만)
        Assert.False(new FocusCheckTask.Evidence(12, 0.5, 2.3, 2.1, 48).TemperatureBad);
        Assert.True(new FocusCheckTask.Evidence(9, -3.3, 2.3, 2.1, 48).TemperatureBad);
        Assert.False(new FocusCheckTask.Evidence(12, 0.5, 2.3, 2.1, 48).HfrBad);
        Assert.True(new FocusCheckTask.Evidence(12, 0.5, 2.9, 2.1, 48).HfrBad);
        Assert.Equal(38, new FocusCheckTask.Evidence(12, 0.5, 2.9, 2.1, 48).GrowthPercent);
        Assert.False(new FocusCheckTask.Evidence(12, 0.5, 2.9, 2.1, 5).HfrBad); // 별이 적으면 비교하지 않음
        Assert.False(new FocusCheckTask.Evidence(null, null, null, 2.1, null).TemperatureBad);
    }

    /// <summary>결과 기록의 모양을 고정한다. 이 시험이 깨지면 그 기록을 읽는 모든 작업을 같이 확인하고 리뷰할 것</summary>
    [Theory]
    [InlineData(typeof(PolarResult), "ErrorArcmin:Nullable`1 ErrorXPx:Double ErrorYPx:Double Grade:String At:DateTimeOffset Skipped:Boolean")]
    [InlineData(typeof(CalibrationResult), "Reused:Boolean OrthogonalityErrorDeg:Nullable`1 Position:String Evening:DateOnly At:DateTimeOffset Skipped:Boolean")]
    [InlineData(typeof(SlewResult), "ArrivalErrorDeg:Double AltitudeDeg:Double At:DateTimeOffset AcceptedHere:Boolean")]
    [InlineData(typeof(FocusResult), "Manual:Boolean Position:Nullable`1 Hfr:Nullable`1 TemperatureC:Nullable`1 At:DateTimeOffset")]
    [InlineData(typeof(CenterResult), "ErrorArcmin:Double CameraAngleDeg:Nullable`1 Attempts:Int32 At:DateTimeOffset Hfr:Nullable`1 Stars:Nullable`1 Skipped:Boolean")]
    [InlineData(typeof(FocusCheckResult), "Refocused:Boolean TemperatureChangeC:Nullable`1 CenterHfr:Nullable`1 Position:Nullable`1 Hfr:Nullable`1 TemperatureC:Nullable`1 At:DateTimeOffset")]
    [InlineData(typeof(GuidingResult), "TotalArcsec:Double RaArcsec:Double DecArcsec:Double Grade:String At:DateTimeOffset Skipped:Boolean")]
    [InlineData(typeof(TestShotResult), "ExposureSeconds:Int32 Hfr:Nullable`1 Eccentricity:Nullable`1 SaturatedPercent:Nullable`1 FilePath:String At:DateTimeOffset Skipped:Boolean")]
    public void 결과_기록의_모양(Type type, string shape)
    {
        var ctor = type.GetConstructors().Single();
        Assert.Equal(shape, string.Join(" ", ctor.GetParameters().Select(p => $"{p.Name}:{p.ParameterType.Name}")));
    }
}
