using Astro.Server.Prepare;
using Astro.Server.Prepare.Flow;
using Astro.Server.Prepare.Sim;
using Astro.Server.Prepare.Tasks.Focus;
using Astro.Server.Shoot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astro.Server.Tests.Prepare;

/// <summary>포커서를 0에 두고 끝내기 → 다음 점검에서 "포커서 0점"을 묻지 않음 (2026-10-09 사용자 요청)</summary>
public class FocuserParkTests
{
    private static readonly SimOptions Fast = new() { Speed = 0 };

    private static (FocuserPark Park, FocuserParkStore Store, SimulatedFocusDevices Devices) Make(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "aira-park-" + Guid.NewGuid().ToString("N"));
        var devices = new SimulatedFocusDevices(Fast, new SimFaults());
        var store = new FocuserParkStore(root);
        var services = new ServiceCollection()
            .AddSingleton(new PrepareFlow([], NullLogger<PrepareRunner>.Instance, simulated: true))
            .BuildServiceProvider();
        var shoot = new ShootSession(new SimulatedShootDevices(Fast, new SimFaults()), new PrepareMode(true), NullLogger<ShootSession>.Instance);
        // 모의 장비로 동작을 보되 모드는 실장비 (모의 모드에서는 기록하지 않음)
        var park = new FocuserPark(new PrepareMode(false), devices, store, services, shoot, NullLogger<FocuserPark>.Instance);
        return (park, store, devices);
    }

    [Fact]
    public async Task 끝낼_때_0으로_보내고_기록하면_다음_점검에서_묻지_않는다()
    {
        var (park, store, devices) = Make();
        Assert.False(await park.ReadyAsync(CancellationToken.None)); // 기록 없음 → 지금처럼 묻는다
        Assert.Null(await park.ParkAsync(CancellationToken.None));
        Assert.Equal(0, await devices.PositionAsync(CancellationToken.None));
        Assert.NotNull(store.ParkedAt);
        Assert.True(await park.ReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 기록이_있어도_포커서가_0이_아니면_묻는다()
    {
        var (park, _, devices) = Make();
        await park.ParkAsync(CancellationToken.None);
        await devices.MoveAsync(800, CancellationToken.None); // 예: N.I.N.A.에서 직접 움직임
        Assert.False(await park.ReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task 기록은_껐다_켜도_남고_지우면_없어진다()
    {
        var root = Path.Combine(Path.GetTempPath(), "aira-park-" + Guid.NewGuid().ToString("N"));
        var (park, store, _) = Make(root);
        await park.ParkAsync(CancellationToken.None);
        Assert.NotNull(new FocuserParkStore(root).ParkedAt);
        store.Clear(); // 포커서가 0을 떠남 (RealFocusDevices 이동·자동초점)
        Assert.Null(new FocuserParkStore(root).ParkedAt);
    }
}
