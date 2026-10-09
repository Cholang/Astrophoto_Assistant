using System.Diagnostics;
using System.Runtime.CompilerServices;
using Astro.Core;
using Astro.Core.Setup;
using Astro.Nina;
using Astro.Server.Setup;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>
/// 1단계 엔진 켜기: N.I.N.A.를 켜고, 연결 통로(Advanced API)가 응답할 때까지 기다린 뒤, 인터넷 연결을 확인한다.
/// 결과 형식은 0단계와 같아서 화면이 같은 쉐브론·공통 영역을 쓴다.
/// </summary>
public sealed class EngineStarter(NinaApiClient nina, InternetCheck internet, NinaWatcher watcher, IOptions<NinaOptions> options)
{
    private static readonly CheckItem Launch = new("launch", "N.I.N.A. 켜기", null,
        $"N.I.N.A.가 꺼져 있으면 {Product.Ga} 대신 켭니다. 처음 켤 때는 1분쯤 걸릴 수 있습니다.");
    private static readonly CheckItem Connect = new("connect", "N.I.N.A. 연결", "Advanced API",
        $"{Product.Ga} N.I.N.A.에 말을 걸고 대답을 받는지 확인합니다.");
    private static readonly CheckItem Internet = new("internet", "인터넷 연결", null,
        "촬영 계획(AI 대화·날씨 예보)에 인터넷이 필요합니다. 촬영이 시작된 뒤에는 끊겨도 촬영은 계속됩니다.");
    private const string AfterNina = "N.I.N.A.와 연결되면 확인합니다.";

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var opt = options.Value;

        // 1. N.I.N.A. 켜기
        var justLaunched = false;
        IDisposable? behind = null; // N.I.N.A.가 켜지며 AA 위로 뜨지 않게 (응답할 때까지 + 조금 더)
        if (Process.GetProcessesByName("NINA").Length is var running and > 0)
        {
            // 여러 개가 켜져 있으면 어느 N.I.N.A.가 Advanced API(1888)를 쓰는지 알 수 없다 (Codex A05) — 아이라가 닫지는 않고 알린다
            yield return running > 1
                ? Launch.Warn($"N.I.N.A.가 {running}개 켜져 있습니다", new Diagnosis(
                    ["N.I.N.A.를 두 번 켰습니다", "다른 프로필로 하나를 더 열었습니다"],
                    "쓰지 않는 N.I.N.A.를 닫고 하나만 남겨 주세요. 아이라는 연결 통로(Advanced API)에 응답하는 N.I.N.A.를 씁니다."))
                : Launch.Pass("이미 켜져 있습니다");
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
                yield return Internet.Skip(AfterNina);
                yield break;
            }

            yield return Launch.Running("N.I.N.A.를 켜는 중입니다");
            string? error = null;
            try
            {
                behind = BackgroundWindows.Watch(TimeSpan.FromSeconds(8), BackgroundWindows.Nina);
                Process.Start(new ProcessStartInfo(ninaExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ninaExe), WindowStyle = ProcessWindowStyle.Minimized });
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
                yield return Internet.Skip(AfterNina);
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

        behind?.Dispose(); // 응답이 오면 조금 더(로딩 끝에 다시 뜨는 창) 지켜본 뒤 끝낸다

        if (version is null)
        {
            yield return Connect.Fail($"N.I.N.A.가 {Product.Name}의 연결에 응답하지 않습니다.", new Diagnosis(
                ["N.I.N.A.의 옵션에서 Advanced API가 꺼져 있습니다.",
                 "Advanced API의 포트가 1888이 아닙니다.",
                 "플러그인을 설치한 뒤 N.I.N.A.를 다시 시작하지 않았습니다."],
                "N.I.N.A.의 옵션(Options) 탭에서 Advanced API가 켜져 있는지와 포트 번호(1888)를 확인한 뒤 새로고침을 눌러 주세요.",
                Detail: $"GET {opt.BaseUrl}version 응답 없음"));
            yield return Internet.Skip(AfterNina);
            yield break;
        }
        yield return Connect.Pass($"연결되었습니다 (Advanced API {version})");
        watcher.Track(); // 이제부터 N.I.N.A.가 꺼지거나 멈추는지 지켜본다

        // 3. 인터넷 연결
        yield return Internet.Running("인터넷 연결을 확인하는 중입니다");
        yield return await internet.IsOnlineAsync(ct)
            ? Internet.Pass("인터넷에 연결되어 있습니다")
            : Internet.Fail("인터넷에 연결되어 있지 않습니다", new Diagnosis(
                ["노트북이 와이파이에 연결되어 있지 않습니다.", "휴대폰 핫스팟이 꺼져 있거나 데이터가 끊겼습니다."],
                "휴대폰 핫스팟을 켜고 노트북을 연결한 뒤 다시 시도를 눌러 주세요."));
    }
}
