using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>N.I.N.A. 로그 → 아이라 알림 (실기 로그 줄 그대로)</summary>
public class NinaLogRulesTests
{
    private static (string Category, string Title, string[] Why, string Fix)? One(params string[] lines) =>
        NinaLogRules.Interpret(NinaLogRules.Parse(lines).First());

    [Fact]
    public void 엠파이어_연결_실패는_전원_허브로()
    {
        var n = One("2026-10-09T12:37:43.9761|ERROR|AscomDevice.cs|Connect|308",
            "ASCOM.DriverException: An error occurred while WandererEmpire was connecting to the device. Please confirm that WandererEmpire can connect to the device correctly.");
        Assert.Equal("connect", n!.Value.Category);
        Assert.Contains("전원 허브", n.Value.Title);
    }

    [Fact]
    public void 없는_COM_포트는_포트_번호와_함께()
    {
        var n = One("2026-10-05T21:00:00.0000|ERROR|AscomDevice.cs|Connect|308", "ASCOM.DriverException: 'COM3' 포트가 없습니다.");
        Assert.Contains("COM3", n!.Value.Title);
    }

    [Fact]
    public void 포커서_멈춤은_자동초점_실패보다_먼저()
    {
        var n = One("2026-10-08T23:40:00.0000|ERROR|AutoFocusVM.cs|StartAutoFocus|500|Failure during AutoFocus",
            "System.Exception: Focuser stuck at position 5833 beyond 00:01:00 timeout");
        Assert.Equal("포커서가 움직이지 않습니다", n!.Value.Title);
    }

    [Fact]
    public void 자동초점_지점_경고와_시작_중_장비_목록_대기는_버린다()
    {
        Assert.Null(One("2026-10-08T23:00:00.0000|WARNING|AutoFocusVM.cs|GetFocusPoints|100|No stars detected in step 3. Setting a high stddev to ignore the point."));
        Assert.Null(One("2026-10-08T21:00:00.0000|ERROR|Connection.cs|DeviceConnect|80", "System.InvalidOperationException: Sequence contains no matching element"));
    }

    [Fact]
    public void 같은_예외라도_다른_곳에서_나면_버리지_않는다_CodexL04()
    {
        Assert.NotNull(One("2026-10-08T21:00:00.0000|ERROR|Different.cs|Critical|80", "System.InvalidOperationException: Sequence contains no matching element"));
        // 아이라는 스위치 값을 바꾸지 않는다 → N.I.N.A.의 실제 실패
        Assert.Equal("switch", One("2026-10-08T23:00:00.0000|ERROR|SwitchVM.cs|SetSwitchValue|100|No switch found for index 3")!.Value.Category);
    }

    [Fact]
    public void 반복_알림은_30초마다_새_번호로_다시_전한다_CodexL06()
    {
        var notices = new NinaNotices();
        var t = DateTimeOffset.Now;
        notices.Add("mount", "적도의와 통신이 끊겼습니다", [], "", "", t);
        var first = notices.Since(0).Single().Id;
        notices.Add("mount", "적도의와 통신이 끊겼습니다", [], "", "", t.AddSeconds(10));
        Assert.Empty(notices.Since(first)); // 30초 전에는 횟수만
        notices.Add("mount", "적도의와 통신이 끊겼습니다", [], "", "", t.AddSeconds(35));
        var again = Assert.Single(notices.Since(first));
        Assert.Equal(3, again.Count);
        Assert.Single(notices.Since(0)); // 같은 알림 하나로 남는다
    }

    [Fact]
    public void 여러_줄_예외는_한_항목으로_묶고_다음_머리_줄에서_끊는다()
    {
        var entries = NinaLogRules.Parse([
            "2026-10-09T12:37:43.9761|ERROR|AscomDevice.cs|Connect|308",
            "ASCOM.DriverException: boom",
            "   at X.Y()",
            "2026-10-09T12:37:44.0008|INFO|AscomDevice.cs|Disconnect|378|Disconnecting"]).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains("boom", entries[0].Body);
        Assert.Equal("INFO", entries[1].Level);
    }

    [Fact]
    public void 같은_알림이_1분_안에_또_오면_횟수만()
    {
        var notices = new NinaNotices();
        var t = DateTimeOffset.Now;
        notices.Add("mount", "적도의와 통신이 끊겼습니다", [], "", "", t);
        notices.Add("mount", "적도의와 통신이 끊겼습니다", [], "", "", t.AddSeconds(5));
        var all = notices.Since(0);
        Assert.Single(all);
        Assert.Equal(2, all[0].Count);
    }
}
