using Astro.Server.Prepare.Real;

namespace Astro.Server.Tests.Real;

/// <summary>저장 사진 찾기 (CX-NIGHT-05): 이미지 폴더 전체에서 이름으로, 저장 시각 앞뒤 2분 안에 쓰인 파일이 하나뿐일 때만</summary>
public class FindSavedFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aa-find-" + Guid.NewGuid().ToString("N"));
    private const string Name = "2026-10-08_21-00-00___120.00s_0000.fits";

    public FindSavedFileTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Put(string dir, DateTime written)
    {
        var d = Path.Combine(_root, dir);
        Directory.CreateDirectory(d);
        var f = Path.Combine(d, Name);
        File.WriteAllText(f, "x");
        File.SetLastWriteTime(f, written);
        return f;
    }

    [Fact]
    public void 깊은_폴더에_있어도_찾는다()
    {
        var f = Put(@"M31\2026-10-08\LIGHT\sub", DateTime.Now);
        for (var i = 0; i < 4; i++) Directory.CreateDirectory(Path.Combine(_root, "newer" + i)); // 더 최근 폴더가 여럿 있어도
        Assert.Equal(f, NinaRig.FindSavedFile(_root, Name, DateTimeOffset.Now));
    }

    [Fact]
    public void 저장_시각과_먼_같은_이름_파일은_고르지_않는다()
    {
        Put("old", DateTime.Now.AddDays(-1));
        Assert.Null(NinaRig.FindSavedFile(_root, Name, DateTimeOffset.Now));
    }

    [Fact]
    public void 후보가_둘이면_옮기지_않게_null()
    {
        Put("a", DateTime.Now);
        Put("b", DateTime.Now);
        Assert.Null(NinaRig.FindSavedFile(_root, Name, DateTimeOffset.Now));
    }
}
