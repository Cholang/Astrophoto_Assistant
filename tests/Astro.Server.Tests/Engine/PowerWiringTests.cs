using Astro.Server.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Engine;

/// <summary>전원 배선 (docs/POWER_LAYOUT_PLAN.md 최종 규칙) — 아이라가 믿는 값, 증거로 조용히 고침</summary>
public class PowerWiringTests
{
    [Fact]
    public void 기본값은_적도의_먼저_카메라는_자체_전원()
    {
        var w = PowerWiring.Default;
        Assert.Equal(PowerWiring.Dc, w.SourceOf("mount"));
        Assert.Equal(PowerWiring.Mount, w.SourceOf(PowerWiring.Hub));
        Assert.Null(w.SourceOf("camera"));
        Assert.False(w.BehindHub("mount"));    // 적도의는 파워박스 앞
        Assert.True(w.BehindHub("focuser"));
        Assert.False(w.BehindHub("camera"));   // 배터리
        Assert.False(w.BehindHub("scope"));    // 전원 없는 칸
        Assert.True(w.BehindHub("newkind") is false); // 모르는 종류는 전원 없음으로
    }

    [Fact]
    public void 순서를_뒤집으면_앞이_되는_쪽이_DC_전원을_받는다()
    {
        var w = PowerWiring.Default.With("mount", PowerWiring.Hub); // 파워박스 먼저
        Assert.Equal(PowerWiring.Hub, w.SourceOf("mount"));
        Assert.Equal(PowerWiring.Dc, w.SourceOf(PowerWiring.Hub));
        Assert.True(w.BehindHub("mount"));
        Assert.Null(PowerWiring.Validate(w.Sources));
    }

    [Fact]
    public void 잘못된_배선은_받지_않는다()
    {
        Assert.NotNull(PowerWiring.Validate(new Dictionary<string, string?> { ["mount"] = PowerWiring.Hub, [PowerWiring.Hub] = PowerWiring.Mount })); // 서로
        Assert.NotNull(PowerWiring.Validate(new Dictionary<string, string?> { ["camera"] = PowerWiring.Dc }));   // DC → 장비 바로
        Assert.NotNull(PowerWiring.Validate(new Dictionary<string, string?> { ["camera"] = "battery" }));        // 모르는 전원
        Assert.Null(PowerWiring.Validate(new Dictionary<string, string?> { ["camera"] = PowerWiring.Mount }));   // 새들 포트
    }

    [Fact]
    public void 프로필을_지우면_목록과_사진이_없어진다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-profile-" + Guid.NewGuid().ToString("N"));
        var profiles = new ProfileStore(root);
        var a = profiles.Create(new NewProfile("지울것", null)).Profile!;
        var b = profiles.Create(new NewProfile("남길것", null)).Profile!;
        Assert.True(profiles.SaveImage(a.Id, [1, 2, 3]));
        Assert.True(profiles.Delete(a.Id));
        Assert.Equal([b.Id], new ProfileStore(root).List().Select(p => p.Id));
        Assert.Null(profiles.ImageFile(a.Id));
        Assert.False(profiles.Delete(a.Id)); // 이미 없음
    }

    [Fact]
    public void 설정하지_않았으면_기본값을_믿고_증거가_있으면_고쳐서_계속_믿는다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-power-" + Guid.NewGuid().ToString("N"));
        var profiles = new ProfileStore(root);
        var p = profiles.Create(new NewProfile("시험", null)).Profile!;
        var store = new PowerWiringStore(profiles, NullLogger<PowerWiringStore>.Instance);
        Assert.Null(store.Current().SourceOf("camera"));
        store.Learn("camera", PowerWiring.Hub, "파워박스 출력을 켜자 나타남");
        Assert.Equal(PowerWiring.Hub, new ProfileStore(root).Get(p.Id)!.Power!.SourceOf("camera")); // 저장됨
        Assert.Equal(PowerWiring.Mount, store.Current().SourceOf(PowerWiring.Hub));               // 나머지는 그대로
        store.Learn(PowerWiring.Hub, PowerWiring.Dc, "파워박스 0V인데 적도의 연결");
        Assert.Equal(PowerWiring.Dc, store.Current().SourceOf(PowerWiring.Hub));
    }
}
