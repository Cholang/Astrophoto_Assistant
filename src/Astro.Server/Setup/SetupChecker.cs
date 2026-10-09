using System.Runtime.CompilerServices;
using Astro.Core;
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

public sealed class SetupOptions
{
    /// <summary>
    /// [임시] 화면 설계용: 실제 설치 여부와 관계없이 모두 "설치되어 있지 않음"으로 보고한다.
    /// 화면의 임시 "설치 완료하기" 버튼과 함께 쓴다. 설계가 끝나면 false로.
    /// </summary>
    public bool SimulateMissing { get; set; }
}

/// <summary>
/// 0단계 설치 확인. 파일과 레지스트리만 본다 — 아무것도 실행하지 않는다.
/// 지금은 사용자 환경(PHD2 가이딩, ASTAP 솔버)에 맞춰 5개 모두 필수로 본다.
/// 다른 가이더·솔버를 쓰는 사람 대응은 배포 단계 과제 (docs/onboarding-and-architecture.md).
/// </summary>
public sealed class SetupChecker(IOptions<NinaOptions> nina, IOptions<SetupOptions> setup)
{
    // 화면은 이 순서대로 쉐브론 칸을 그린다 (web/src/screens/SetupCheckScreen.tsx와 같은 순서).
    // 칸에는 실제 설치할 프로그램 이름만, 무엇을 하는 프로그램인지는 Hint(공통 영역)에.
    // Diagnosis.Fix는 상태 옆 ? 툴팁, ActionLabel/ActionUrl은 설치 링크로 쓰인다.
    private static readonly (CheckItem Item, Diagnosis HowToInstall)[] Items =
    [
        (new("ascom", "ASCOM Platform", null,
            "여러 제조사의 망원경·카메라 드라이버가 공통으로 쓰는 기반 프로그램입니다."),
         new([], "ASCOM 공식 페이지에서 설치 파일을 받아 설치한 뒤, 새로고침을 눌러 주세요. 설치 후 PC를 다시 시작해야 할 수 있습니다.",
             "ASCOM Platform 내려받기", "https://ascom-standards.org/Downloads/Index.htm")),
        (new("nina", "N.I.N.A.", null,
            $"실제로 장비를 움직이고 사진을 찍는 프로그램입니다. {Product.Neun} N.I.N.A.를 통해 장비를 다룹니다."),
         new([], "N.I.N.A. 공식 페이지에서 설치한 뒤 새로고침을 눌러 주세요. 기본 위치가 아닌 곳에 설치했다면 설정에서 경로를 지정할 수 있습니다.",
             "N.I.N.A. 내려받기", "https://nighttime-imaging.eu/download/")),
        (new("advanced-api", "Advanced API", null,
            $"{Product.Ga} N.I.N.A.에 명령을 보내고 상태를 받는 통로입니다. N.I.N.A. 플러그인으로 설치합니다."),
         new([], "N.I.N.A.를 열고 플러그인(Plugins) 탭에서 'Advanced API'를 찾아 설치한 뒤, N.I.N.A.를 다시 시작하고 새로고침을 눌러 주세요.",
             "Advanced API 안내 페이지", "https://github.com/christian-photo/ninaAPI")),
        (new("phd2", "PHD2", null,
            "보조 카메라로 별 하나를 계속 지켜보며 망원경의 작은 흔들림을 바로잡는 프로그램입니다."),
         new([], "PHD2 공식 페이지에서 설치한 뒤 새로고침을 눌러 주세요.",
             "PHD2 내려받기", "https://openphdguiding.org/downloads/")),
        (new("astap", "ASTAP", null,
            "사진 속 별 배치를 분석해 망원경이 실제로 어디를 보고 있는지 계산하는 프로그램입니다(플레이트 솔빙)."),
         new([], "ASTAP과 별 데이터(예: D50)를 함께 설치한 뒤 새로고침을 눌러 주세요.",
             "ASTAP 내려받기", "https://www.hnsky.org/astap.htm")),
    ];

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (item, howToInstall) in Items)
            yield return Check(item, howToInstall);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 전체를 한 번에 확인한다 (프로필을 고른 직후). 모두 설치돼 있으면 화면은 0단계를 건너뛴다.
    /// 파일·레지스트리만 보므로 순식간에 끝난다.
    /// </summary>
    public IReadOnlyList<CheckResult> CheckAll() => Items.Select(x => Check(x.Item, x.HowToInstall)).ToList();

    /// <summary>한 항목만 다시 확인한다 (쉐브론 안의 새로고침). 없는 id면 null.</summary>
    public CheckResult? CheckOne(string id)
    {
        foreach (var (item, howToInstall) in Items)
            if (item.Id == id) return Check(item, howToInstall);
        return null;
    }

    private CheckResult Check(CheckItem item, Diagnosis howToInstall)
    {
        var installed = !setup.Value.SimulateMissing && IsInstalled(item.Id);
        return installed
            ? item.Pass("설치되어 있습니다")
            : item.Fail("설치되어 있지 않습니다", howToInstall);
    }

    private bool IsInstalled(string id) => id switch
    {
        "ascom" => InstallLocator.AscomPlatformVersion() is not null,
        "nina" => InstallLocator.NinaExe(nina.Value.ExePath) is not null,
        "advanced-api" => InstallLocator.AdvancedApiPluginInstalled(),
        "phd2" => InstallLocator.Phd2Installed(null),
        "astap" => InstallLocator.AstapInstalled(null),
        _ => false,
    };
}

/// <summary>체크 항목 하나의 이름과 설명. 결과를 만들 때 같은 이름을 쓰게 한다.</summary>
internal sealed record CheckItem(string Id, string Title, string? Term, string? Hint, CheckSeverity Severity = CheckSeverity.Required)
{
    public CheckResult Pass(string message) => Make(CheckStatus.Pass, message);
    public CheckResult Running(string message) => Make(CheckStatus.Running, message);
    public CheckResult Skip(string message) => Make(CheckStatus.Skipped, message);
    public CheckResult Fail(string message, Diagnosis diagnosis) => Make(CheckStatus.Fail, message, diagnosis);
    public CheckResult Warn(string message, Diagnosis diagnosis) => Make(CheckStatus.Warn, message, diagnosis);
    private CheckResult Make(CheckStatus status, string message, Diagnosis? diagnosis = null) =>
        new(Id, Title, Term, Hint, Severity, status, message, diagnosis);
}
