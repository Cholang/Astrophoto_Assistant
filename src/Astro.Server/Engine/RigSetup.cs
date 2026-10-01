using System.Text.Json;
using Astro.Core;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>AA에서 고른 장비 (드라이버 Id와 이름)</summary>
public sealed record RigChoice(string Id, string Name);

/// <summary>장비 고르기의 결과: 바뀐 칸과, 실패했으면 화면에 보여 줄 문장</summary>
public sealed record RigSelectResult(CheckResult Item, string? Error);

/// <summary>
/// AA가 **확인하고 연결한** 장비 (종류별 드라이버 Id). N.I.N.A.의 "연결됨"은 어떤 장비인지 알려 주지 않으므로,
/// 연결된 장비가 고른 장비와 같다고 믿어도 되는지를 여기로 판단한다. 앱을 다시 켜면 비어 있다.
/// </summary>
public sealed class LiveDevices
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _ids = [];

    public string? Get(string kind)
    {
        lock (_gate) return _ids.GetValueOrDefault(kind);
    }

    public void Set(string kind, string id)
    {
        lock (_gate) _ids[kind] = id;
    }

    public void Forget(string kind)
    {
        lock (_gate) _ids.Remove(kind);
    }
}

/// <summary>
/// AA 쪽 장비 선택. N.I.N.A. 프로필 위에 덮어서 읽는다 (EquipmentConnector).
/// 시뮬레이션에서는 N.I.N.A.를 건드리지 않고 여기에만 저장하고, 실제로는 N.I.N.A.에 쓰지 못했을 때만 남긴다.
/// 값이 null이면 "제거" (프로필에 있어도 없는 것으로 본다). 데이터 폴더의 rig-overrides.json에 저장.
/// </summary>
public sealed class RigOverrides
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private Dictionary<string, RigChoice?> _map;

    public RigOverrides(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "rig-overrides.json");
        _map = Load();
    }

    /// <summary>덮어쓴 값이 있으면 true. choice가 null이면 "제거됨"</summary>
    public bool TryGet(string kind, out RigChoice? choice)
    {
        lock (_gate) return _map.TryGetValue(kind, out choice);
    }

    public void Set(string kind, RigChoice? choice)
    {
        lock (_gate)
        {
            _map[kind] = choice;
            Save();
        }
    }

    public void Clear(string kind)
    {
        lock (_gate)
        {
            if (_map.Remove(kind)) Save();
        }
    }

    private Dictionary<string, RigChoice?> Load()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize<Dictionary<string, RigChoice?>>(File.ReadAllText(_file)) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// 장비 변경 모드: 설치된 드라이버 목록을 주고, 고른 장비를 저장한다.
/// 드라이버 목록은 N.I.N.A.가 켜져 있어야 받을 수 있다 (장비 연결 화면에서만 쓰는 이유).
/// 드라이버의 첫 설정 창(포트 번호 등)은 API로 열 수 없다 — 연결 실패 안내가 N.I.N.A. 장비 탭의 톱니바퀴를 알려 준다.
/// </summary>
public sealed class RigSetup(NinaApiClient nina, RigOverrides overrides, LiveDevices live, EquipmentConnector connector, IOptions<EquipmentOptions> options, OpticsStore optics)
{
    public async Task<IReadOnlyList<RigChoice>?> DevicesAsync(string kind, CancellationToken ct)
    {
        if (EquipmentConnector.FindSlot(kind) is null) return null;
        var list = await nina.ListDevicesAsync(kind, ct);
        // "없음" 항목(No_Device, No_Guider)은 화면의 "제거"가 맡는다
        return list.Where(d => !d.Id.StartsWith("No_", StringComparison.Ordinal)).Select(d => new RigChoice(d.Id, d.Name)).ToList();
    }

    /// <summary>
    /// 장비 고르기 (deviceId가 null이면 제거). 바뀐 장비의 칸을 돌려준다. 잘못된 요청이면 null.
    /// 실제 모드에서는 **N.I.N.A.에서 실제로 바뀐 것이 확인될 때만** 선택을 저장한다 — 실패하면 원래 장비를 그대로 두고
    /// (끊었다면 다시 연결해 두고) 실패 문장을 돌려준다. 화면의 이름과 실제 연결된 장비가 어긋나지 않게 (2026-09-30 리뷰).
    /// </summary>
    public async Task<RigSelectResult?> SelectAsync(string kind, string? deviceId, string? name, CancellationToken ct)
    {
        if (EquipmentConnector.FindSlot(kind) is not { } slot) return null;
        if (deviceId is null && slot.Tier == CheckSeverity.Required) return null; // 카메라·적도의·망원경은 제거할 수 없다
        // 망원경: 목록에서 고르기만 (N.I.N.A.에는 "변경 완료" 뒤 다시 확인할 때 써 넣는다)
        if (kind == EquipmentConnector.Scope)
            return optics.Use(deviceId!) && await connector.DescribeAsync(kind, ct) is { } scopeItem ? new RigSelectResult(scopeItem, null) : null;
        var choice = deviceId is null ? null : new RigChoice(deviceId, string.IsNullOrWhiteSpace(name) ? deviceId : name);

        string? error = null;
        if (options.Value.Simulate)
        {
            overrides.Set(kind, choice);
        }
        else if (choice is not null)
        {
            error = await SwitchAsync(slot, choice, ct);
        }
        else
        {
            error = await RemoveAsync(slot, ct);
        }
        return await connector.DescribeAsync(kind, ct) is { } item ? new RigSelectResult(item, error) : null;
    }

    /// <summary>다른 장비로 바꾸기: 지금 장비를 끊고 → 새 장비 연결 → 연결 확인. 실패하면 원래 장비를 다시 연결해 둔다</summary>
    private async Task<string?> SwitchAsync(EquipmentConnector.Slot slot, RigChoice choice, CancellationToken ct)
    {
        var kind = slot.Kind;
        var previous = await connector.CurrentIdAsync(kind, ct);
        var wasConnected = await nina.IsConnectedAsync(kind, ct);
        // "연결됨"만으로는 어떤 장비인지 알 수 없으므로, 지금 연결된 것은 먼저 끊는다
        if (wasConnected) await nina.DisconnectAsync(kind, ct);

        // 실기 미검증: connect?to=로 고르면 N.I.N.A.가 그 장비를 쓰고 프로필에 저장한다
        if (await nina.ConnectAsync(kind, choice.Id, ct) && await nina.IsConnectedAsync(kind, ct))
        {
            overrides.Set(kind, choice); // AA가 고른 장비 (N.I.N.A. 프로필 저장이 안 되어도 AA는 이 장비를 쓴다)
            live.Set(kind, choice.Id);
            return null;
        }

        // 실패: 선택은 저장하지 않고, 원래 연결돼 있었다면 원래 장비로 되돌린다
        live.Forget(kind);
        if (wasConnected && previous is not null && await nina.ConnectAsync(kind, previous, ct) && await nina.IsConnectedAsync(kind, ct))
            live.Set(kind, previous);
        return $"{choice.Name} 연결에 실패해 바꾸지 않았습니다. 원래 장비를 그대로 씁니다. 장비 전원·케이블과 드라이버 설정(N.I.N.A. 장비 탭의 톱니바퀴)을 확인해 주세요.";
    }

    /// <summary>제거: 연결을 끊고 "연결 안 됨"이 확인될 때만 없는 장비로 바꾼다</summary>
    private async Task<string?> RemoveAsync(EquipmentConnector.Slot slot, CancellationToken ct)
    {
        var kind = slot.Kind;
        var off = !await nina.IsConnectedAsync(kind, ct)
            || (await nina.DisconnectAsync(kind, ct) && !await nina.IsConnectedAsync(kind, ct));
        if (!off)
            return $"{slot.Role} 연결을 끊지 못해 제거하지 않았습니다. N.I.N.A.에서 연결을 끊은 뒤 다시 시도해 주세요.";

        overrides.Set(kind, null);
        live.Forget(kind);
        // 실기 미검증: 프로필 값도 "없음"으로 (실패해도 AA 쪽 선택으로 없는 장비로 다룬다)
        await nina.ChangeProfileValueAsync($"{slot.ProfileKey}-{(kind == "guider" ? "GuiderName" : "Id")}", kind == "guider" ? "No_Guider" : "No_Device", ct);
        return null;
    }
}
