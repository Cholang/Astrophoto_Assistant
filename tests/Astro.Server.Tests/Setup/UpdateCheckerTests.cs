using Astro.Server.Setup;

namespace Astro.Server.Tests.Setup;

/// <summary>새 버전 알림의 버전 비교 (앞 몇 자리만 숫자로)</summary>
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("3.3.0.1001", "3.2.0.9001", 4, 1)]
    [InlineData("3.2.0.9001", "3.2.0.9001", 4, 0)]
    [InlineData("7.1.3", "7.1.3.4851", 3, 0)]   // ASCOM: 태그 7.1.3 = 설치 7.1.3.4851
    [InlineData("2.6.14", "2.6.9", 3, 1)]        // 문자열이 아니라 숫자로
    [InlineData("2.2.15.2", "2.2.16.0", 4, -1)]
    public void 버전_비교(string a, string b, int parts, int expected) =>
        Assert.Equal(expected, Math.Sign(UpdateChecker.Compare(a, b, parts)));
}
