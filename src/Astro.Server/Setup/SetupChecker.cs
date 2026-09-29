using System.Runtime.CompilerServices;
using Astro.Core.Setup;
using Microsoft.Extensions.Options;

namespace Astro.Server.Setup;

public sealed class NinaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:1888/v2/api/";
    /// <summary>비워 두면 레지스트리와 기본 설치 경로에서 찾는다.</summary>
    public string? ExePath { get; set; }
    public bool LaunchIfNotRunning { get; set; } = true;
    /// <summary>NINA를 직접 켠 뒤 API가 응답할 때까지 기다리는 최대 시간.</summary>
    public int StartupTimeoutSeconds { get; set; } = 90;
}

/// <summary>
/// 0단계 설치 확인. 파일과 레지스트리만 본다 — 아무것도 실행하지 않는다.
/// 지금은 사용자 환경(PHD2 가이딩, ASTAP 솔버)에 맞춰 5개 모두 필수로 본다.
/// 다른 가이더·솔버를 쓰는 사람 대응은 배포 단계 과제 (onboarding-and-architecture.md).
/// </summary>
public sealed class SetupChecker(IOptions<NinaOptions> options)
{
    private const string AscomUrl = "https://ascom-standards.org/Downloads/Index.htm";
    private const string NinaUrl = "https://nighttime-imaging.eu/download/";
    private const string AdvancedApiUrl = "https://github.com/christian-photo/ninaAPI";
    private const string Phd2Url = "https://openphdguiding.org/downloads/";
    private const string AstapUrl = "https://www.hnsky.org/astap.htm";

    // 화면은 이 순서대로 쉐브론 칸을 그린다 (web/src/screens/SetupCheckScreen.tsx와 같은 순서).
    private static readonly CheckItem Ascom = new("ascom", "장비 드라이버 기반", "ASCOM Platform",
        "여러 제조사의 망원경·카메라 드라이버가 공통으로 쓰는 기반 프로그램입니다.");
    private static readonly CheckItem Nina = new("nina", "촬영 엔진", "N.I.N.A.",
        "실제로 장비를 움직이고 사진을 찍는 프로그램입니다. AA는 N.I.N.A.를 통해 장비를 다룹니다.");
    private static readonly CheckItem AdvancedApi = new("advanced-api", "N.I.N.A. 연결 통로", "Advanced API",
        "AA가 N.I.N.A.에 명령을 보내고 상태를 받는 통로입니다. N.I.N.A. 플러그인으로 설치합니다.");
    private static readonly CheckItem Phd2 = new("phd2", "가이딩 프로그램", "PHD2",
        "보조 카메라로 별 하나를 계속 지켜보며 망원경의 작은 흔들림을 바로잡는 프로그램입니다.");
    private static readonly CheckItem Astap = new("astap", "별 위치 분석", "ASTAP",
        "사진 속 별 배치를 분석해 망원경이 실제로 어디를 보고 있는지 계산하는 프로그램입니다(플레이트 솔빙).");

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var ascom = InstallLocator.AscomPlatformVersion();
        yield return ascom is null
            ? Ascom.Fail("ASCOM Platform이 설치되어 있지 않습니다.", new Diagnosis(
                ["이 PC에 아직 설치하지 않았습니다.", "설치가 끝나지 않았거나, 설치 후 PC를 다시 시작하지 않았습니다."],
                "ASCOM 공식 페이지에서 설치 파일을 받아 설치한 뒤, 다시 확인을 눌러 주세요.",
                "ASCOM 설치 페이지 열기", AscomUrl,
                @"HKLM\SOFTWARE\WOW6432Node\ASCOM\Platform, HKLM\SOFTWARE\ASCOM\Platform 에서 'Platform Version'을 찾지 못함"))
            : Ascom.Pass($"설치되어 있습니다 (버전 {ascom})");

        var ninaExe = InstallLocator.NinaExe(options.Value.ExePath);
        yield return ninaExe is null
            ? Nina.Fail("N.I.N.A.가 설치되어 있지 않습니다.", new Diagnosis(
                ["이 PC에 아직 설치하지 않았습니다.", "기본 위치가 아닌 곳에 설치했습니다."],
                "N.I.N.A. 공식 페이지에서 설치한 뒤 다시 확인을 눌러 주세요. 다른 위치에 설치했다면 설정에서 경로를 지정할 수 있습니다.",
                "N.I.N.A. 설치 페이지 열기", NinaUrl,
                "설치 목록(레지스트리)과 기본 설치 경로에서 NINA.exe를 찾지 못함"))
            : Nina.Pass("설치되어 있습니다");

        yield return InstallLocator.AdvancedApiPluginInstalled()
            ? AdvancedApi.Pass("N.I.N.A. 플러그인으로 설치되어 있습니다")
            : AdvancedApi.Fail("Advanced API 플러그인이 설치되어 있지 않습니다.", new Diagnosis(
                ["N.I.N.A.에 이 플러그인을 아직 설치하지 않았습니다.", "N.I.N.A.를 새 버전으로 바꾸면서 플러그인이 빠졌습니다."],
                "N.I.N.A.를 열고 플러그인(Plugins) 탭에서 'Advanced API'를 찾아 설치한 뒤, N.I.N.A.를 다시 시작하고 다시 확인을 눌러 주세요.",
                "설치 안내 보기", AdvancedApiUrl,
                @"%LOCALAPPDATA%\NINA\Plugins\<버전>\Advanced API 폴더를 찾지 못함"));

        yield return InstallLocator.Phd2Installed(null)
            ? Phd2.Pass("설치되어 있습니다")
            : Phd2.Fail("PHD2가 설치되어 있지 않습니다.", new Diagnosis(
                ["이 PC에 아직 설치하지 않았습니다.", "기본 위치(Program Files (x86)\\PHDGuiding2)가 아닌 곳에 설치했습니다."],
                "PHD2 공식 페이지에서 설치한 뒤 다시 확인을 눌러 주세요.",
                "PHD2 설치 페이지 열기", Phd2Url,
                @"%ProgramFiles(x86)%\PHDGuiding2\phd2.exe 없음, 설치 목록에도 없음"));

        yield return InstallLocator.AstapInstalled(null)
            ? Astap.Pass("설치되어 있습니다. 별 데이터는 첫 분석 때 확인합니다")
            : Astap.Fail("ASTAP이 설치되어 있지 않습니다.", new Diagnosis(
                ["이 PC에 아직 설치하지 않았습니다.", "기본 위치(Program Files\\astap)가 아닌 곳에 설치했습니다."],
                "ASTAP과 별 데이터(예: D50)를 함께 설치한 뒤 다시 확인을 눌러 주세요. 없으면 망원경을 대상 한가운데로 자동으로 맞출 수 없습니다.",
                "ASTAP 설치 페이지 열기", AstapUrl,
                @"%ProgramFiles%\astap\astap.exe 없음, 설치 목록에도 없음"));

        await Task.CompletedTask;
    }
}

/// <summary>체크 항목 하나의 이름과 설명. 결과를 만들 때 같은 이름을 쓰게 한다.</summary>
internal sealed record CheckItem(string Id, string Title, string? Term, string? Hint, CheckSeverity Severity = CheckSeverity.Required)
{
    public CheckResult Pass(string message) => Make(CheckStatus.Pass, message);
    public CheckResult Running(string message) => Make(CheckStatus.Running, message);
    public CheckResult Skip(string message) => Make(CheckStatus.Skipped, message);
    public CheckResult Fail(string message, Diagnosis diagnosis) => Make(CheckStatus.Fail, message, diagnosis);
    private CheckResult Make(CheckStatus status, string message, Diagnosis? diagnosis = null) =>
        new(Id, Title, Term, Hint, Severity, status, message, diagnosis);
}
