using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

public sealed class EquipmentOptions
{
    /// <summary>
    /// [임시] 장비 없이 개발할 때: 지금 N.I.N.A. 프로필에 있는 장비가 모두 연결된 것으로 간주한다.
    /// 실제 연결은 하지 않는다. 장비를 연결할 수 있게 되면 false.
    /// </summary>
    public bool Simulate { get; set; }

    /// <summary>
    /// [임시] 시뮬레이션에서 연결 실패로 시작할 장비 (switch, mount, camera, focuser, filterwheel, rotator, flatdevice, guider). "*"는 전부.
    /// 화면에 들어올 때마다 이 목록으로 다시 시작하고, 장비별 "다시 연결"을 누르면 그 장비는 성공한다.
    /// </summary>
    public string[] SimulateFailing { get; set; } = [];
}

/// <summary>
/// [임시] 시뮬레이션의 "아직 해결 안 된 장비" 목록. 화면이 여러 번 요청해도 유지되도록 서버에 하나만 둔다.
/// </summary>
public sealed class EquipmentSimulation
{
    private readonly Lock _gate = new();
    private HashSet<string> _failing = [];
    private readonly HashSet<string> _fixed = [];
    private bool _failAll;

    /// <summary>화면에 들어올 때: 실패로 시작할 장비를 다시 정하고, 해결 기록은 지운다.</summary>
    public void Reset(string[] failing)
    {
        lock (_gate)
        {
            _failAll = failing.Contains("*");
            _failing = [.. failing];
            _fixed.Clear();
        }
    }

    public bool IsFailing(string id)
    {
        lock (_gate) return !_fixed.Contains(id) && (_failAll || _failing.Contains(id));
    }

    /// <summary>"다시 연결"을 누르면 그 장비는 해결된 것으로 본다.</summary>
    public void Fix(string id)
    {
        lock (_gate) _fixed.Add(id);
    }
}

/// <summary>
/// 사용자가 "없이 진행"을 고른 준필수 장비 (포커서·가이더). 뒤 단계(계획 추천 등)가 읽는다.
/// 장비 연결 화면에 들어올 때마다 비운다.
/// </summary>
public sealed class EquipmentChoices
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _without = [];

    public void Reset()
    {
        lock (_gate) _without.Clear();
    }

    public void GoWithout(string id)
    {
        lock (_gate) _without.Add(id);
    }

    public void Connected(string id)
    {
        lock (_gate) _without.Remove(id);
    }

    public bool IsWithout(string id)
    {
        lock (_gate) return _without.Contains(id);
    }
}

/// <summary>
/// 장비 연결. 지금 쓰는 N.I.N.A. 프로필에서 장비 목록을 읽어, 전원 허브부터 차례로 연결한다.
/// 장비 등급 (DESIGN.md 3장 "장비 연결"):
/// - 필수(카메라·적도의): 프로필에 없거나 연결에 실패하면 멈춘다
/// - 준필수(포커서·가이더): 프로필에 없으면 통과, 실패하면 사용자가 "없이 진행"을 고를 수 있다
/// - 선택(전원 허브·필터휠·회전장치·플랫패널): 프로필에 없으면 통과, 실패하면 경고만 하고 계속
///   단 전원 허브는 다른 장비에 전원을 주므로, 있으면 맨 먼저 연결하고 실패하면 뒤 장비는 보류한다
/// 돔·안전 모니터·날씨 장치는 다루지 않는다.
/// </summary>
public sealed class EquipmentConnector(NinaApiClient nina, IOptions<EquipmentOptions> options, EquipmentSimulation sim, EquipmentChoices choices, RigOverrides overrides, LiveDevices live, OpticsStore optics, EquipmentRun state, DevicePrecheck.IHostDevices host, ILogger<EquipmentConnector> log)
{
    private const CheckSeverity Required = CheckSeverity.Required;
    private const CheckSeverity Recommended = CheckSeverity.Recommended;
    private const CheckSeverity Optional = CheckSeverity.Optional;

    /// <summary>연결 순서: 허브가 다른 장비에 전원을 주므로 가장 먼저 (docs/onboarding-and-architecture.md 4.2).</summary>
    // Fix: 연결에 실패했을 때 상태 옆 ? 툴팁에 보여 줄 해결 방법 (장비마다 다르게)
    private static readonly Slot[] Slots =
    [
        new("switch", "SwitchSettings", Optional, "전원 허브", "망원경·카메라·열선에 전원을 나눠 주는 장치입니다. 열선은 허브의 포트로 함께 제어합니다.",
            "허브의 12V 전원 어댑터와 PC로 가는 USB 케이블을 확인해 주세요. 허브가 켜져야 다른 장비에도 전원이 들어갑니다."),
        new("mount", "TelescopeSettings", Required, "적도의", "망원경을 움직이고 별을 따라 돌려 주는 받침대입니다.",
            "적도의 전원과 케이블(USB·네트워크)을 확인해 주세요. 무선으로 연결한다면 PC가 적도의의 와이파이에 연결되어 있는지도 확인해 주세요."),
        // 망원경: 연결되는 장비가 아니라 AA의 망원경 목록(OpticsStore)에서 고른 것. 초점거리가 있어야 화각·플레이트 솔빙이 된다 (2026-10-01)
        new(Scope, "", Required, "망원경", "빛을 모으는 망원경(또는 렌즈)입니다. 초점거리와 F값으로 화각과 노출을 계산합니다.",
            "아래 \"장비 변경\"에서 망원경 원을 눌러 쓰는 망원경을 고르거나 추가해 주세요."),
        new("camera", "CameraSettings", Required, "카메라", "사진을 찍는 카메라입니다.",
            "카메라 전원과 USB 케이블을 확인하고, 카메라의 PC 연결 방식이 테더링(PC 촬영)으로 되어 있는지 확인해 주세요."),
        new("focuser", "FocuserSettings", Recommended, "포커서", "초점을 자동으로 맞춰 주는 모터입니다. 없으면 초점을 손으로 맞춥니다.",
            "포커서 전원(허브 포트)과 USB 케이블을 확인해 주세요."),
        new("filterwheel", "FilterWheelSettings", Optional, "필터휠", "촬영 중에 필터를 바꿔 끼워 주는 장치입니다. 없으면 필터를 손으로 끼웁니다.",
            "필터휠 전원과 USB 케이블을 확인해 주세요."),
        new("rotator", "RotatorSettings", Optional, "회전장치", "카메라를 돌려 구도의 각도를 맞춰 주는 장치입니다. 없으면 손으로 돌립니다.",
            "회전장치 전원과 USB 케이블을 확인해 주세요."),
        new("flatdevice", "FlatDeviceSettings", Optional, "플랫패널", "플랫(밝기 고르게 맞추기용 사진)을 찍을 때 망원경 앞을 고르게 비추는 판입니다.",
            "플랫패널 전원과 USB 케이블을 확인해 주세요."),
        new("guider", "GuiderSettings", Recommended, "가이딩", "보조 카메라로 별을 지켜보며 흔들림을 바로잡습니다. 없으면 노출을 짧게 찍습니다.",
            "PHD2가 켜져 있는지, PHD2 안에서 가이드 카메라와 적도의가 연결되어 있는지 확인해 주세요."),
    ];

    public sealed record Slot(string Kind, string ProfileKey, CheckSeverity Tier, string Role, string Hint, string Fix);

    public static Slot? FindSlot(string kind) => Slots.FirstOrDefault(s => s.Kind == kind);

    /// <summary>허브가 실패하면 뒤 장비는 이 문장으로 보류한다 (전원이 허브에서 오므로 원인을 하나로 모은다).</summary>
    private const string WaitingForHub = "전원 허브가 연결되면 확인합니다";

    private const string Hub = "switch";

    public const string Scope = "scope";

    /// <summary>화면에 미리 칸을 그릴 수 있게, 연결할 장비 목록만 먼저 알려 준다.</summary>
    /// 화면에 들어올 때마다 부르므로, 시뮬레이션의 실패 목록과 "없이 진행" 기록도 여기서 처음 상태로 되돌린다.
    public async Task<IReadOnlyList<CheckResult>> PlanAsync(CancellationToken ct = default)
    {
        if (options.Value.Simulate) sim.Reset(options.Value.SimulateFailing);
        choices.Reset();
        return (await ReadDevicesAsync(ct)).Select(d => d.Absent ? Make(d, CheckStatus.Absent, "") : Pending(d)).ToList();
    }

    // ── 연결 중 종료 (2026-10-09 사용자 요청): AA를 끄면 연결을 바로 멈추고, 이번에 연결하던 장비의 연결을 끊은 것을 확인한 뒤 닫는다.
    // 연결 요청을 취소해도 N.I.N.A.는 하던 연결을 끝까지 하므로(실기: AA가 꺼진 뒤 "스위치 연결됨") 끊고, 늦게 붙는지도 잠시 지켜본다
    // 연결기는 요청마다 새로 만들어지므로(Transient) 진행 상태는 앱에 하나인 EquipmentRun에 둔다
    // (2026-10-09 시험: 상태를 연결기에 두었더니 종료 요청이 연결 중인 것을 보지 못함)
    private Lock _gate => state.Gate;
    private CancellationTokenSource? _runCts { get => state.Cts; set => state.Cts = value; }
    private TaskCompletionSource? _runDone { get => state.Done; set => state.Done = value; }
    private List<string> _touched => state.Touched;

    private CancellationToken BeginRun(CancellationToken ct, bool fresh)
    {
        lock (_gate)
        {
            if (fresh) _touched.Clear();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _runCts.Token;
        }
    }

    private void EndRun()
    {
        lock (_gate)
        {
            _runDone?.TrySetResult();
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void Touch(string kind)
    {
        lock (_gate) if (!_touched.Contains(kind)) _touched.Add(kind);
    }

    /// <summary>
    /// 종료 전: 연결 중이면 멈추고, 이번에 연결을 시도한 장비를 끊고 끊긴 것을 확인한다 (전원 허브는 맨 나중).
    /// 연결 중이 아니었으면 아무것도 하지 않는다. 끊지 못한 장비가 있으면 그 이름들
    /// </summary>
    public async Task<IReadOnlyList<string>> AbortAsync(CancellationToken ct)
    {
        Task done;
        List<string> kinds;
        lock (_gate)
        {
            if (_runCts is null) return [];
            _runCts.Cancel();
            done = _runDone?.Task ?? Task.CompletedTask;
            kinds = [.. _touched];
            _touched.Clear();
        }
        try { await done.WaitAsync(TimeSpan.FromSeconds(5), ct); } catch (TimeoutException) { }
        if (options.Value.Simulate) return [];

        // 모든 장비를 함께 1초마다 보며 붙어 있으면 끊는다 (허브는 다른 장비가 다 끊긴 뒤에). 3번 연속 모두 끊긴 채면 끝.
        // 하던 연결이 늦게 붙으면 다시 끊는다 (최대 30초) — 장비마다 차례로 지켜보면 17초 걸렸음 (2026-10-09 시험)
        var order = Slots.Select(s => s.Kind).Reverse().Where(kinds.Contains).ToList();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var quiet = 0;
        var still = new List<string>();
        while (quiet < 3 && DateTime.UtcNow < deadline)
        {
            still.Clear();
            foreach (var kind in order)
                if (await nina.IsConnectedAsync(kind, ct)) still.Add(kind);
            if (still.Count == 0) quiet++;
            else
            {
                quiet = 0;
                var others = still.Where(k => k != Hub).ToList();
                await Task.WhenAll((others.Count > 0 ? others : still).Select(k => nina.DisconnectAsync(k, ct)));
            }
            await Task.Delay(1000, ct);
        }
        foreach (var kind in order.Except(still)) live.Forget(kind);
        return still.Select(k => FindSlot(k)?.Role ?? k).ToList();
    }

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken outer = default)
    {
        var ct = BeginRun(outer, fresh: true);
        try
        {
        var devices = await ReadDevicesAsync(ct);
        if (devices.Count == 0)
        {
            yield return new CheckResult("profile", "N.I.N.A. 프로필", null, null, CheckSeverity.Required, CheckStatus.Fail,
                "장비 설정을 읽지 못했습니다", new Diagnosis([], "N.I.N.A.가 켜져 있고 장비가 프로필에 등록되어 있는지 확인한 뒤 새로고침을 눌러 주세요."));
            yield break;
        }

        var hubFailed = false;
        foreach (var d in devices)
        {
            // 허브가 실패하면 멈추고 허브부터 해결한다: 뒤 장비는 연결을 시도하지 않고 보류
            // 등록되지 않은 장비: 연결하지 않고 작은 원으로만 보인다
            if (d.Absent)
            {
                yield return Make(d, CheckStatus.Absent, "");
                continue;
            }
            if (hubFailed)
            {
                yield return Make(d, CheckStatus.Skipped, WaitingForHub);
                continue;
            }
            if (d.Id is not null) yield return Make(d, CheckStatus.Running, "연결하는 중입니다");
            var result = await ConnectOneAsync(d, ct);
            yield return result;
            if (d.Slot.Kind == Hub && result.Status != CheckStatus.Pass) hubFailed = true;
        }
        }
        finally { EndRun(); }
    }

    /// <summary>
    /// 장비 하나만 다시 연결한다.
    /// simulateFail: [임시] 화면 설계용 — 실패했을 때와 똑같은 결과를 만든다 (Simulate가 켜져 있을 때만).
    /// </summary>
    public async Task<CheckResult?> RetryAsync(string id, bool simulateFail = false, CancellationToken ct = default)
    {
        var d = (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == id);
        if (d is null) return null;
        // [임시] 시뮬레이션: 사용자가 원인을 해결하고 "다시 연결"을 눌렀다고 보고, 이 장비는 이제 성공한다
        if (options.Value.Simulate && !simulateFail) sim.Fix(id);
        var token = BeginRun(ct, fresh: false);
        try { return await ConnectOneAsync(d, token, simulateFail && options.Value.Simulate); }
        finally { EndRun(); }
    }

    /// <summary>장비 하나의 지금 모습 (연결 전). 장비 변경 모드에서 고른 뒤 칸을 바꿀 때</summary>
    public async Task<CheckResult?> DescribeAsync(string kind, CancellationToken ct = default) =>
        (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == kind) is { } d
            ? d.Absent ? Make(d, CheckStatus.Absent, "") : Pending(d)
            : null;

    /// <summary>지금 이 종류로 쓰는 장비의 드라이버 Id (AA에서 고른 것 → 프로필). 없으면 null</summary>
    public async Task<string?> CurrentIdAsync(string kind, CancellationToken ct = default) =>
        (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == kind)?.Id;

    /// <summary>준필수 장비를 "없이 진행". 필수·선택 장비는 대상이 아니다.</summary>
    public async Task<CheckResult?> GoWithoutAsync(string id, CancellationToken ct = default)
    {
        var d = (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == id);
        if (d is null || d.Slot.Tier != Recommended) return null;
        choices.GoWithout(id);
        return Make(d, CheckStatus.Warn, $"{d.Slot.Role} 없이 진행합니다");
    }

    private async Task<CheckResult> ConnectOneAsync(Device d, CancellationToken ct, bool forceFail = false)
    {
        if (d.Absent) return Make(d, CheckStatus.Absent, "");

        // 망원경: 연결 대신 고른 망원경의 초점거리·F값을 N.I.N.A.에 써 넣는다 (시뮬레이션이어도 — N.I.N.A.가 화각·솔빙에 쓴다)
        if (d.Slot.Kind == Scope)
        {
            if (optics.Current is not { } scope)
                return Make(d, CheckStatus.Fail, "등록된 망원경이 없습니다", new Diagnosis([], d.Slot.Fix));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            await nina.ChangeProfileValueAsync("TelescopeSettings-FocalLength", Math.Round(scope.EffectiveFocalLength, 1).ToString(inv), ct);
            await nina.ChangeProfileValueAsync("TelescopeSettings-FocalRatio", Math.Round(scope.EffectiveFocalRatio, 2).ToString(inv), ct);
            await nina.ChangeProfileValueAsync("TelescopeSettings-Name", scope.Name, ct);
            return Make(d, CheckStatus.Pass, scope.Optics);
        }

        // 필수 장비가 프로필에 없음: 연결을 시도할 수 없다
        if (d.Id is null)
            return Make(d, CheckStatus.Fail, $"N.I.N.A.에 등록된 {d.Slot.Role}{Josa(d.Slot.Role)} 없습니다", new Diagnosis([],
                $"아래 \"장비 변경\"에서 {d.Slot.Role}{Reul(d.Slot.Role)} 등록해 주세요. 처음 쓰는 드라이버라면 N.I.N.A.의 장비 탭에서 드라이버 설정(톱니바퀴)도 한 번 해 주세요."));

        bool connected;
        if (forceFail)
        {
            connected = false;
        }
        else if (options.Value.Simulate)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), ct); // 연결되는 모습이 보이게 잠깐 기다린다
            connected = !sim.IsFailing(d.Slot.Kind);
        }
        else
        {
            // 실기 미검증: 장비를 연결할 수 있는 환경에서 확인 필요
            // N.I.N.A.의 "연결됨"은 어떤 장비인지 알려 주지 않는다. 그래서 "이미 연결됨"을 믿는 것은
            //  - AA가 이 장비를 연결했다고 기록해 둔 경우, 또는
            //  - AA에서 따로 고른 적이 없어 N.I.N.A. 프로필 그대로인 경우(N.I.N.A.가 연결한 것 = 프로필 장비)만.
            // 그 밖에는(AA에서 다른 장비를 골랐는데 확인된 적 없음) 지금 연결을 끊고 고른 장비로 새로 연결한다 (2026-09-30 리뷰)
            var kind = d.Slot.Kind;
            Touch(kind);
            using var behind = BackgroundWindows.ForConnect(kind); // PHD2·Wanderer Empire가 켜지며 AA 위로 뜨지 않게
            var connectedNow = await nina.IsConnectedAsync(kind, ct);
            var known = live.Get(kind);
            var trust = connectedNow && (known == d.Id || (known is null && !d.Chosen));
            if (trust)
            {
                connected = true;
            }
            else
            {
                // 연결 직전 점검 (docs/PRECHECK_DESIGN.md ③): 장비가 PC에 보이지 않으면 N.I.N.A.에 맡기지 않고 할 일을 알린다 —
                // N.I.N.A. 오류 알림·2분 기다림 없이. 허브 USB 출력을 막 켰을 수 있어 USB 장치는 10초까지 기다려 본다
                if (!connectedNow)
                {
                    if (kind == Hub && DevicePrecheck.EmpireStale(host))
                    {
                        // Empire가 켜진 뒤 USB가 다시 꽂힘 → Empire가 허브와 끊겨 있을 수 있다. 닫아 두면 N.I.N.A. 연결이 새로 띄우며 자동으로 연결 (10/09 실기)
                        log.LogInformation("Empire가 켜진 뒤 USB 장치가 새로 꽂혀 Empire를 다시 켭니다");
                        await Task.Run(host.CloseEmpire, ct);
                    }
                    var pre = await DevicePrecheck.CheckAsync(host, kind, d.Id, d.Slot.Role, TimeSpan.FromSeconds(10), ct);
                    if (!pre.Ok)
                    {
                        live.Forget(kind);
                        var diag = new Diagnosis([], $"{pre.Fix} 그다음 다시 연결을 눌러 주세요.");
                        return d.Slot.Tier == Optional && kind != Hub
                            ? Make(d, CheckStatus.Warn, $"{pre.Message} — {d.Slot.Role} 없이 진행합니다", diag)
                            : Make(d, CheckStatus.Fail, pre.Message!, diag);
                    }
                }
                if (connectedNow) await nina.DisconnectAsync(kind, ct);
                await WaitListedAsync(kind, d.Id, ct);
                connected = await nina.ConnectAsync(kind, d.Id, ct) && await nina.IsConnectedAsync(kind, ct);
            }
            // 허브가 연결되면 USB 출력을 켜 둔다 — 가이드 카메라·포커서가 허브 USB에 붙어 있다 (자동 해결, 다시 읽어 확인 — 10/09: N.I.N.A.는 실패해도 성공이라 답함)
            if (connected && kind == Hub) await EnsureHubUsbOnAsync(ct);
            if (connected) live.Set(kind, d.Id);
            else live.Forget(kind);

            // 가이더: N.I.N.A.가 PHD2 프로그램에 붙은 것만으로는 부족 — PHD2 안의 카메라·적도의까지 직접 확인 (2026-10-06)
            if (connected && kind == "guider" && await CheckPhd2Async(ct) is { } problem)
                return Make(d, CheckStatus.Fail, problem.Message, new Diagnosis([], problem.Fix));
        }

        if (connected)
        {
            choices.Connected(d.Slot.Kind);
            return Make(d, CheckStatus.Pass, "연결되어 있습니다");
        }

        var diagnosis = new Diagnosis([],
            $"{d.Slot.Fix} 그다음 다시 연결을 눌러 주세요. 처음 쓰는 장비라면 N.I.N.A.의 장비 탭에서 드라이버 설정(톱니바퀴)을 먼저 해 주세요. (드라이버: {d.DriverName})");
        // 선택 장비는 경고만 (허브는 뒤 장비의 전원이라 실패로 둔다)
        return d.Slot.Tier == Optional && d.Slot.Kind != Hub
            ? Make(d, CheckStatus.Warn, $"연결되지 않아 {d.Slot.Role} 없이 진행합니다", diagnosis)
            : Make(d, CheckStatus.Fail, "연결되어 있지 않습니다", diagnosis);
    }

    /// <summary>
    /// 허브의 "USB" 출력이 꺼져 있으면 켠다. N.I.N.A. 출력 바꾸기의 index는 출력 Id가 아니라 쓰기 가능한 출력 목록의 순서(10/09 실기 —
    /// Id를 넣으면 "Switch value updated"라고 답하고 실제로는 안 바뀜) → 순서로 바꾸고 다시 읽어 확인한다. 못 켜도 연결은 그대로 (뒤 장비 점검이 알린다)
    /// </summary>
    private async Task EnsureHubUsbOnAsync(CancellationToken ct)
    {
        var info = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
        if (!info.Ok || info.Response is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("WritableSwitches", out var ws) || ws.ValueKind != JsonValueKind.Array) return;
        var list = ws.EnumerateArray().ToList();
        var index = list.FindIndex(s => Text(s, "Name") is { } n && Regex.IsMatch(n, @"(^|:\s*)USB\s*$|^USB\b", RegexOptions.IgnoreCase));
        if (index < 0) return;
        if (list[index].TryGetProperty("Value", out var v) && v.TryGetDouble(out var value) && value >= 1) return;
        await nina.RequestAsync($"equipment/switch/set?index={index}&value=1", TimeSpan.FromSeconds(10), ct);
        // N.I.N.A.는 출력 값을 몇 초마다 새로 읽는다(10/09 실기: 켠 뒤 2초에는 아직 0) → 10초까지 1초마다 다시 읽는다
        var on = false;
        for (var i = 0; i < 10 && !on; i++)
        {
            await Task.Delay(1000, ct);
            var after = await nina.RequestAsync("equipment/switch/info", TimeSpan.FromSeconds(10), ct);
            on = after.Response is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("WritableSwitches", out var w2) && w2.ValueKind == JsonValueKind.Array
                && w2.EnumerateArray().ElementAtOrDefault(index) is { ValueKind: JsonValueKind.Object } sw && sw.TryGetProperty("Value", out var v2) && v2.TryGetDouble(out var val2) && val2 >= 1;
        }
        log.LogInformation(on ? "허브 USB 출력을 켰습니다" : "허브 USB 출력을 켜지 못했습니다 (다시 읽은 값이 꺼짐)");
    }

    /// <summary>PHD2 주소는 N.I.N.A. 프로필의 가이더 설정에서(없으면 localhost:4400), 비교할 적도의 이름은 N.I.N.A.가 연결한 적도의</summary>
    /// <summary>
    /// N.I.N.A.가 그 장비를 장비 목록에 올릴 때까지 기다린다 (최대 30초). N.I.N.A.를 막 켰을 때는 몇 초 동안 장비 목록을 만드는 중이라
    /// connect?to=가 "목록에 없음"(Sequence contains no matching element)으로 실패한다 (2026-10-08 실기: 켜고 9초 뒤 전원 허브 연결 실패, 목록은 11초 뒤 완성).
    /// 끝내 안 나타나면 그냥 연결을 시도해 원래 오류를 보인다
    /// </summary>
    private async Task WaitListedAsync(string kind, string id, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if ((await nina.ListDevicesAsync(kind, ct)).Any(x => x.Id == id)) return;
            await Task.Delay(1500, ct);
        }
    }

    private async Task<Phd2Check.Problem?> CheckPhd2Async(CancellationToken ct)
    {
        var host = "localhost";
        var port = 4400;
        if (await nina.GetActiveProfileAsync(ct) is { ValueKind: JsonValueKind.Object } profile && profile.TryGetProperty("GuiderSettings", out var g))
        {
            if (Text(g, "PHD2ServerUrl") is { } h) host = h;
            if (g.TryGetProperty("PHD2ServerPort", out var p) && p.TryGetInt32(out var n)) port = n;
        }
        var mount = await nina.GetInfoAsync("mount", ct) is { ValueKind: JsonValueKind.Object } m
            && m.TryGetProperty("Connected", out var c) && c.ValueKind == JsonValueKind.True ? Text(m, "Name") : null;
        return await Phd2Check.CheckAsync(host, port, mount, ct);
    }

    /// <summary>Id가 null이면 등록되지 않은 장비. 필수 장비면 실패, 나머지는 Absent(작은 원)</summary>
    /// <summary>Chosen: AA의 장비 변경에서 고른 장비 (N.I.N.A. 프로필과 다를 수 있다)</summary>
    private sealed record Device(Slot Slot, string? Id, string Name, string DriverName, bool Chosen = false)
    {
        public bool Absent => Id is null && Slot.Tier != Required;
    }

    private async Task<List<Device>> ReadDevicesAsync(CancellationToken ct)
    {
        if (await nina.GetActiveProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } profile) return [];
        var list = new List<Device>();
        foreach (var slot in Slots)
        {
            if (slot.Kind == Scope)
            {
                // 칸 아래 작은 글씨: 이름만 (초점거리·F값은 장비 변경의 목록에서, 2026-10-01 사용자 결정)
                list.Add(optics.Current is { } scope
                    ? new Device(slot, scope.Id, scope.Name, scope.Name)
                    : new Device(slot, null, "등록 안 됨", ""));
                continue;
            }
            // AA에서 고른 장비가 있으면 그것을 쓴다 (null = 제거)
            if (overrides.TryGet(slot.Kind, out var chosen))
            {
                list.Add(chosen is null
                    ? new Device(slot, null, "등록 안 됨", "")
                    : new Device(slot, chosen.Id, ShortName(slot.Kind, chosen.Id, chosen.Name), chosen.Name, Chosen: true));
                continue;
            }
            // 가이더는 Id 대신 GuiderName에 들어 있다 (예: PHD2_Single)
            var id = profile.TryGetProperty(slot.ProfileKey, out var s) ? Text(s, slot.Kind == "guider" ? "GuiderName" : "Id") : null;
            if (id is null || id is "No_Device" or "No_Guider")
            {
                // 등록 안 됨: 필수 장비는 실패 칸, 나머지는 작은 원 (장비 변경 모드에서 등록할 수 있게 목록에는 넣는다)
                list.Add(new Device(slot, null, "등록 안 됨", ""));
                continue;
            }
            var last = Text(s, "LastDeviceName");
            list.Add(new Device(slot, id, ShortName(slot.Kind, id, last), last ?? id));
        }
        return list;
    }

    /// <summary>칸에 쓸 짧은 이름: "WandererEmpire WandererBox 1 (ASCOM)" → "WandererBox", "X-T5 (gin_X-T5)" → "X-T5"</summary>
    private static string ShortName(string kind, string id, string? last)
    {
        if (kind == "guider") return id.StartsWith("PHD2", StringComparison.OrdinalIgnoreCase) ? "PHD2" : id;
        var name = last ?? id;
        var paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) name = name[..paren];
        foreach (var noise in new[] { "WandererEmpire ", "ASCOM " }) name = name.Replace(noise, "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+\d+$", ""); // "WandererBox 1" → "WandererBox"
        return name.Trim();
    }

    private static string? Text(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    // 장비 이름 뒤 조사 (카메라가 / 적도의가 — 모두 받침 없음이지만 이름이 바뀌어도 맞게)
    private static string Josa(string word) => Astro.Core.Josa.HasFinalConsonant(word) ? "이" : "가";
    private static string Reul(string word) => Astro.Core.Josa.HasFinalConsonant(word) ? "을" : "를";

    private static CheckResult Pending(Device d) => Make(d, CheckStatus.Pending, "");

    private static CheckResult Make(Device d, CheckStatus status, string message, Diagnosis? diagnosis = null) =>
        // 칸 가운데에는 장비 종류(적도의 등), 그 아래 작게 장비 이름(OnStep 등)
        new(d.Slot.Kind, d.Slot.Role, d.Name, d.Slot.Hint, d.Slot.Tier, status, message, diagnosis);
}

/// <summary>장비 연결 진행 상태 (앱에 하나): 연결 취소용 토큰, 끝남 신호, 이번에 연결을 시도한 장비</summary>
public sealed class EquipmentRun
{
    public Lock Gate { get; } = new();
    public CancellationTokenSource? Cts { get; set; }
    public TaskCompletionSource? Done { get; set; }
    public List<string> Touched { get; } = [];
}
