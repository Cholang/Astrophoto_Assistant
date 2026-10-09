using Astro.Core;

namespace Astro.Server.Tests.Engine;

/// <summary>이름을 바꾸면 예전 데이터 폴더를 새 폴더로 옮긴다 (2026-10-09 AA → 아이라)</summary>
public class ProductDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aa-move-" + Guid.NewGuid().ToString("N"));

    public ProductDataTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void 예전_폴더를_통째로_옮긴다()
    {
        var old = Path.Combine(_root, "AA");
        Directory.CreateDirectory(Path.Combine(old, "profile-images"));
        File.WriteAllText(Path.Combine(old, "profiles.json"), "[]");
        File.WriteAllText(Path.Combine(old, "profile-images", "a.webp"), "x");
        var now = Path.Combine(_root, "AIRA");
        Product.MoveData(old, now);
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(now, "profiles.json")));
        Assert.True(File.Exists(Path.Combine(now, "profile-images", "a.webp")));
    }

    [Fact]
    public void 새_폴더가_있으면_건드리지_않는다()
    {
        var old = Path.Combine(_root, "AA");
        var now = Path.Combine(_root, "AIRA");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "profiles.json"), "old");
        Directory.CreateDirectory(now);
        File.WriteAllText(Path.Combine(now, "profiles.json"), "new");
        Product.MoveData(old, now);
        Assert.Equal("new", File.ReadAllText(Path.Combine(now, "profiles.json")));
        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public void 예전_폴더가_없으면_아무것도_안_한다()
    {
        var now = Path.Combine(_root, "AIRA");
        Product.MoveData(Path.Combine(_root, "AA"), now);
        Assert.False(Directory.Exists(now));
    }
}
