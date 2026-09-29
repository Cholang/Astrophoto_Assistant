using System.Runtime.CompilerServices;
using System.Text.Json;
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
    /// [임시] 시뮬레이션에서 연결 실패로 시작할 장비 (switch, mount, camera, focuser, guider). "*"는 전부.
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
/// 장비 연결. 지금 쓰는 N.I.N.A. 프로필에서 장비 목록을 읽어, 전원 허브부터 차례로 연결한다.
/// 프로필에서 "없음"으로 된 장비(필터휠 등)는 건너뛴다.
/// </summary>
public sealed class EquipmentConnector(NinaApiClient nina, IOptions<EquipmentOptions> options, EquipmentSimulation sim)
{
    /// <summary>연결 순서: 허브가 다른 장비에 전원을 주므로 가장 먼저 (onboarding-and-architecture.md 4.2).</summary>
    // Fix: 연결에 실패했을 때 상태 옆 ? 툴팁에 보여 줄 해결 방법 (장비마다 다르게)
    private static readonly Slot[] Slots =
    [
        new("switch", "SwitchSettings", "전원 허브", "망원경·카메라·열선에 전원을 나눠 주는 장치입니다. 열선은 허브의 포트로 함께 제어합니다.",
            "허브의 12V 전원 어댑터와 PC로 가는 USB 케이블을 확인해 주세요. 허브가 켜져야 다른 장비에도 전원이 들어갑니다."),
        new("mount", "TelescopeSettings", "적도의", "망원경을 움직이고 별을 따라 돌려 주는 받침대입니다.",
            "적도의 전원과 케이블(USB·네트워크)을 확인해 주세요. 무선으로 연결한다면 PC가 적도의의 와이파이에 연결되어 있는지도 확인해 주세요."),
        new("camera", "CameraSettings", "카메라", "사진을 찍는 카메라입니다.",
            "카메라 전원과 USB 케이블을 확인하고, 카메라의 PC 연결 방식이 테더링(PC 촬영)으로 되어 있는지 확인해 주세요."),
        new("focuser", "FocuserSettings", "포커서", "초점을 자동으로 맞춰 주는 모터입니다.",
            "포커서 전원(허브 포트)과 USB 케이블을 확인해 주세요."),
        new("filterwheel", "FilterWheelSettings", "필터휠", "촬영 중에 필터를 바꿔 끼워 주는 장치입니다.",
            "필터휠 전원과 USB 케이블을 확인해 주세요."),
        new("guider", "GuiderSettings", "가이딩", "보조 카메라로 별을 지켜보며 흔들림을 바로잡습니다. N.I.N.A.가 PHD2를 거쳐 가이드 카메라와 실제로 연결되는지 확인합니다.",
            "PHD2가 켜져 있는지, PHD2 안에서 가이드 카메라와 적도의가 연결되어 있는지 확인해 주세요."),
    ];

    private sealed record Slot(string Kind, string ProfileKey, string Role, string Hint, string Fix);

    /// <summary>허브가 실패하면 뒤 장비는 이 문장으로 보류한다 (전원이 허브에서 오므로 원인을 하나로 모은다).</summary>
    private const string WaitingForHub = "전원 허브가 연결되면 확인합니다";

    /// <summary>화면에 미리 칸을 그릴 수 있게, 연결할 장비 목록만 먼저 알려 준다.</summary>
    /// 화면에 들어올 때마다 부르므로, 시뮬레이션의 실패 목록도 여기서 처음 상태로 되돌린다.
    public async Task<IReadOnlyList<CheckResult>> PlanAsync(CancellationToken ct = default)
    {
        if (options.Value.Simulate) sim.Reset(options.Value.SimulateFailing);
        return (await ReadDevicesAsync(ct)).Select(d => Pending(d)).ToList();
    }

    public async IAsyncEnumerable<CheckResult> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
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
            if (hubFailed)
            {
                yield return Make(d, CheckStatus.Skipped, WaitingForHub);
                continue;
            }
            yield return Make(d, CheckStatus.Running, "연결하는 중입니다");
            var result = await ConnectOneAsync(d, ct);
            yield return result;
            if (d.Slot.Kind == "switch" && result.Status != CheckStatus.Pass) hubFailed = true;
        }
    }

    /// <summary>
    /// 장비 하나만 다시 연결한다 (쉐브론 안의 새로고침).
    /// simulateFail: [임시] 화면 설계용 — 실패했을 때와 똑같은 결과를 만든다 (Simulate가 켜져 있을 때만).
    /// </summary>
    public async Task<CheckResult?> RetryAsync(string id, bool simulateFail = false, CancellationToken ct = default)
    {
        var d = (await ReadDevicesAsync(ct)).FirstOrDefault(x => x.Slot.Kind == id);
        if (d is null) return null;
        // [임시] 시뮬레이션: 사용자가 원인을 해결하고 "다시 연결"을 눌렀다고 보고, 이 장비는 이제 성공한다
        if (options.Value.Simulate && !simulateFail) sim.Fix(id);
        return await ConnectOneAsync(d, ct, simulateFail && options.Value.Simulate);
    }

    private async Task<CheckResult> ConnectOneAsync(Device d, CancellationToken ct, bool forceFail = false)
    {
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
            connected = await nina.IsConnectedAsync(d.Slot.Kind, ct)
                || (await nina.ConnectAsync(d.Slot.Kind, d.Id, ct) && await nina.IsConnectedAsync(d.Slot.Kind, ct));
        }

        return connected
            ? Make(d, CheckStatus.Pass, "연결되어 있습니다")
            : Make(d, CheckStatus.Fail, "연결되어 있지 않습니다", new Diagnosis([],
                $"{d.Slot.Fix} 그다음 새로고침을 눌러 주세요. 처음 쓰는 장비라면 N.I.N.A.의 장비 탭에서 드라이버 설정(톱니바퀴)을 먼저 해 주세요. (드라이버: {d.DriverName})"));
    }

    private sealed record Device(Slot Slot, string Id, string Name, string DriverName);

    private async Task<List<Device>> ReadDevicesAsync(CancellationToken ct)
    {
        if (await nina.GetActiveProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } profile) return [];
        var list = new List<Device>();
        foreach (var slot in Slots)
        {
            if (!profile.TryGetProperty(slot.ProfileKey, out var s)) continue;
            // 가이더는 Id 대신 GuiderName에 들어 있다 (예: PHD2_Single)
            var id = Text(s, slot.Kind == "guider" ? "GuiderName" : "Id");
            if (id is null || id is "No_Device" or "No_Guider") continue;
            var last = Text(s, "LastDeviceName");
            list.Add(new Device(slot, id, ShortName(slot.Kind, id, last), last ?? id));
        }
        return list;
    }

    /// <summary>쉐브론에 쓸 짧은 이름: "WandererEmpire WandererBox 1 (ASCOM)" → "WandererBox", "X-T5 (gin_X-T5)" → "X-T5"</summary>
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

    private static CheckResult Pending(Device d) => Make(d, CheckStatus.Pending, "");

    private static CheckResult Make(Device d, CheckStatus status, string message, Diagnosis? diagnosis = null) =>
        // 칸 가운데에는 장비 종류(적도의 등), 그 아래 작게 장비 이름(OnStep 등)
        new(d.Slot.Kind, d.Slot.Role, d.Name, d.Slot.Hint, CheckSeverity.Required, status, message, diagnosis);
}
