using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Tasks.Calibration;
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
        Assert.Equal("redo:focus", TestShotRules.Judge(ok with { Hfr = 3.0 }, 2.1, 120).Problem!.RedoAction);
        Assert.Equal("redo:guiding", TestShotRules.Judge(ok with { Eccentricity = .8 }, 2.1, 120).Problem!.RedoAction);
    }

    /// <summary>결과 기록의 모양을 고정한다. 이 시험이 깨지면 그 기록을 읽는 모든 작업을 같이 확인하고 리뷰할 것</summary>
    [Theory]
    [InlineData(typeof(PolarResult), "ErrorArcmin:Nullable`1 ErrorXPx:Double ErrorYPx:Double Grade:String At:DateTimeOffset")]
    [InlineData(typeof(CalibrationResult), "Reused:Boolean OrthogonalityErrorDeg:Nullable`1 Position:String Evening:DateOnly At:DateTimeOffset")]
    [InlineData(typeof(SlewResult), "ArrivalErrorDeg:Double AltitudeDeg:Double At:DateTimeOffset")]
    [InlineData(typeof(FocusResult), "Manual:Boolean Position:Nullable`1 Hfr:Nullable`1 TemperatureC:Nullable`1 At:DateTimeOffset")]
    [InlineData(typeof(CenterResult), "ErrorArcmin:Double CameraAngleDeg:Nullable`1 Attempts:Int32 At:DateTimeOffset")]
    [InlineData(typeof(GuidingResult), "TotalArcsec:Double RaArcsec:Double DecArcsec:Double Grade:String At:DateTimeOffset")]
    [InlineData(typeof(TestShotResult), "ExposureSeconds:Int32 Hfr:Nullable`1 Eccentricity:Nullable`1 SaturatedPercent:Nullable`1 FilePath:String At:DateTimeOffset")]
    public void 결과_기록의_모양(Type type, string shape)
    {
        var ctor = type.GetConstructors().Single();
        Assert.Equal(shape, string.Join(" ", ctor.GetParameters().Select(p => $"{p.Name}:{p.ParameterType.Name}")));
    }
}
