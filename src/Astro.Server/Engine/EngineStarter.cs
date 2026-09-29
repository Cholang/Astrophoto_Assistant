using System.Diagnostics;
using System.Runtime.CompilerServices;
using Astro.Core;
using Astro.Core.Setup;
using Astro.Nina;
using Astro.Server.Setup;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>
/// 1단계 엔진 켜기: N.I.N.A.를 켜고, 연결 통로(Advanced API)가 응답할 때까지 기다린다.
/// 결과 형식은 0단계와 같아서 화면이 같은 쉐브론·공통 영역을 쓴다.
/// </summary>
public sealed class EngineStarter(NinaApiClient nina, IOptions<NinaOptions> options)
{
    private static readonly CheckItem Launch = new("launch", "N.I.N.A. 켜기", null,
        $"N.I.N.A.가 꺼져 있으면 {Product.Ga} 대신 켭니다.");
    private static readonly CheckItem Connect = new("connect", "연결 통로 응답", "Advanced API",
        $"{Product.Ga} N.I.N.A.에 말을 걸고 대답을 받는지 확인합니다.");

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var opt = options.Value;

        // 1. N.I.N.A. 켜기
        var justLaunched = false;
        if (Process.GetProcessesByName("NINA").Length > 0)
        {
            yield return Launch.Pass("이미 켜져 있습니다");
        }
        else
        {
            var ninaExe = InstallLocator.NinaExe(opt.ExePath);
            if (ninaExe is null || !opt.LaunchIfNotRunning)
            {
                yield return Launch.Fail("N.I.N.A.가 꺼져 있습니다.", new Diagnosis(
                    ninaExe is null ? ["N.I.N.A. 실행 파일을 찾지 못했습니다."] : [$"설정에서 {Product.Ga} N.I.N.A.를 자동으로 켜지 않도록 해 두었습니다."],
                    "N.I.N.A.를 직접 실행한 뒤 새로고침을 눌러 주세요."));
                yield return Connect.Skip("N.I.N.A.가 켜지면 확인합니다.");
                yield break;
            }

            yield return Launch.Running("N.I.N.A.를 켜는 중입니다");
            string? error = null;
            try
            {
                Process.Start(new ProcessStartInfo(ninaExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ninaExe) });
                justLaunched = true;
            }
            catch (Exception e) { error = e.Message; }

            if (error is not null)
            {
                yield return Launch.Fail("N.I.N.A.를 켜지 못했습니다.", new Diagnosis(
                    ["N.I.N.A. 설치 파일이 손상되었습니다.", "보안 프로그램이 실행을 막았습니다."],
                    "N.I.N.A.를 직접 실행해 보고, 켜지면 새로고침을 눌러 주세요.",
                    Detail: $"{ninaExe}\n{error}"));
                yield return Connect.Skip("N.I.N.A.가 켜지면 확인합니다.");
                yield break;
            }
            yield return Launch.Pass($"{Product.Ga} N.I.N.A.를 켰습니다");
        }

        // 2. 연결 통로 응답 대기
        yield return Connect.Running(justLaunched ? "N.I.N.A.가 준비되기를 기다리는 중입니다" : "응답을 확인하는 중입니다");
        var deadline = DateTime.UtcNow.AddSeconds(justLaunched ? opt.StartupTimeoutSeconds : 5);
        string? version;
        while (true)
        {
            version = await nina.GetVersionAsync(ct);
            if (version is not null || DateTime.UtcNow >= deadline) break;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        yield return version is not null
            ? Connect.Pass($"연결되었습니다 (Advanced API {version})")
            : Connect.Fail($"N.I.N.A.가 {Product.Name}의 연결에 응답하지 않습니다.", new Diagnosis(
                ["N.I.N.A.의 옵션에서 Advanced API가 꺼져 있습니다.",
                 "Advanced API의 포트가 1888이 아닙니다.",
                 "플러그인을 설치한 뒤 N.I.N.A.를 다시 시작하지 않았습니다."],
                "N.I.N.A.의 옵션(Options) 탭에서 Advanced API가 켜져 있는지와 포트 번호(1888)를 확인한 뒤 새로고침을 눌러 주세요.",
                Detail: $"GET {opt.BaseUrl}version 응답 없음"));
    }
}
