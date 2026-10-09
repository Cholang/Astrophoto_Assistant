using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>연결 직후 점검: 사진 저장 폴더·솔버·시뮬레이터 (docs/PRECHECK_DESIGN.md ④⑤)</summary>
public class PostConnectCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aira-post-" + Guid.NewGuid().ToString("N"));
    public PostConnectCheckTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void 쓸_수_있는_폴더는_통과() => Assert.Null(DevicePrecheck.Storage(_root));

    [Fact]
    public void 없는_폴더는_막는다()
    {
        var n = DevicePrecheck.Storage(Path.Combine(_root, "없음"));
        Assert.True(n!.Block);
        Assert.Contains("없습니다", n.Message);
    }

    [Fact]
    public void 동기화_폴더는_알리고_진행()
    {
        var od = Path.Combine(_root, "OneDrive", "문서");
        Directory.CreateDirectory(od);
        var n = DevicePrecheck.Storage(od);
        Assert.False(n!.Block);
        Assert.Contains("동기화", n.Message);
    }

    [Fact]
    public void 모르는_경로는_확인하지_않는다() => Assert.Null(DevicePrecheck.Storage(null));

    [Fact]
    public void ASTAP과_별_목록이_있으면_통과()
    {
        var exe = Path.Combine(_root, "astap.exe");
        File.WriteAllText(exe, "");
        File.WriteAllText(Path.Combine(_root, "d50_0101.1476"), "");
        Assert.Null(DevicePrecheck.Solver("ASTAP", exe));
    }

    [Fact]
    public void ASTAP이_없거나_별_목록이_없으면_알린다()
    {
        Assert.Contains("찾지 못했습니다", DevicePrecheck.Solver("ASTAP", Path.Combine(_root, "astap.exe"))!.Message);
        var exe = Path.Combine(_root, "astap.exe");
        File.WriteAllText(exe, "");
        Assert.Contains("별 목록", DevicePrecheck.Solver("ASTAP", exe)!.Message);
        Assert.Null(DevicePrecheck.Solver("PlateSolve3", null)); // 다른 솔버는 모름 → 막지 않음
    }

    [Fact]
    public void 적도의_관측지가_다르면_알린다_CodexD02()
    {
        Assert.Null(DevicePrecheck.MountSite(37.3313, 127.1102, 37.3311, 127.1103));
        Assert.Contains("관측지가 다릅니다", DevicePrecheck.MountSite(37.33, 127.11, 37.82, 127.14)!.Message); // 포천 소흘에 남아 있던 값
        Assert.Null(DevicePrecheck.MountSite(37.33, 127.11, double.NaN, 0)); // 모르면 확인 안 함
    }

    [Fact]
    public void 시뮬레이터는_막지_않고_알린다()
    {
        var n = DevicePrecheck.Simulator("적도의", "Telescope Simulator for .NET");
        Assert.False(n!.Block);
        Assert.Null(DevicePrecheck.Simulator("적도의", "On-Step"));
    }
}
