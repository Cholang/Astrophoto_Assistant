using System.Text;
using Astro.Server.Setup;

namespace Astro.Server.Tests.Setup;

/// <summary>PHD2 실행 파일 안의 "PHD2 Guiding 2.6.14"를 1MB씩 읽어 찾기 (파일 전체를 문자열로 만들지 않음)</summary>
public class Phd2VersionTests
{
    private static string Write(int offset, string text = "PHD2 Guiding 2.6.14")
    {
        var path = Path.GetTempFileName();
        var bytes = new byte[offset + 200];
        new Random(1).NextBytes(bytes);
        Encoding.Unicode.GetBytes(text).CopyTo(bytes, offset);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData(10)]                  // 처음
    [InlineData(1001)]                // 홀수 자리 (예전 방식은 짝수 자리만 읽혔음)
    [InlineData((1 << 20) - 20)]      // 첫 조각 경계에 걸침
    [InlineData((1 << 20) * 2 + 33)]  // 세 번째 조각
    public void 어디에_있어도_버전을_찾는다(int offset)
    {
        var path = Write(offset);
        try { Assert.Equal("2.6.14", UpdateChecker.FindUtf16Version(path, "PHD2 Guiding ")); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 버전_형식이_아니면_다음_것을_찾고_없으면_null()
    {
        var path = Write(500, "PHD2 Guiding by ... PHD2 Guiding 2.7.1");
        try { Assert.Equal("2.7.1", UpdateChecker.FindUtf16Version(path, "PHD2 Guiding ")); }
        finally { File.Delete(path); }
        path = Write(500, "nothing here");
        try { Assert.Null(UpdateChecker.FindUtf16Version(path, "PHD2 Guiding ")); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 설치된_PHD2()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PHDGuiding2", "phd2.exe");
        if (!File.Exists(exe)) return;
        Assert.Matches(@"^\d+\.\d+\.\d+$", UpdateChecker.FindUtf16Version(exe, "PHD2 Guiding ")!);
    }
}
