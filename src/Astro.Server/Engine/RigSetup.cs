using System.Text.Json;
using Astro.Core;
using Astro.Core.Setup;
using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Engine;

/// <summary>AA에서 고른 장비 (드라이버 Id와 이름)</summary>
public sealed record RigChoice(string Id, string Name);

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
public sealed class RigSetup(NinaApiClient nina, RigOverrides overrides, EquipmentConnector connector, IOptions<EquipmentOptions> options)
{
    public async Task<IReadOnlyList<RigChoice>?> DevicesAsync(string kind, CancellationToken ct)
    {
        if (EquipmentConnector.FindSlot(kind) is null) return null;
        var list = await nina.ListDevicesAsync(kind, ct);
        // "없음" 항목(No_Device, No_Guider)은 화면의 "제거"가 맡는다
        return list.Where(d => !d.Id.StartsWith("No_", StringComparison.Ordinal)).Select(d => new RigChoice(d.Id, d.Name)).ToList();
    }

    /// <summary>장비 고르기 (deviceId가 null이면 제거). 바뀐 장비의 칸(연결 전 상태)을 돌려준다. 잘못된 요청이면 null</summary>
    public async Task<CheckResult?> SelectAsync(string kind, string? deviceId, string? name, CancellationToken ct)
    {
        if (EquipmentConnector.FindSlot(kind) is not { } slot) return null;
        if (deviceId is null && slot.Tier == CheckSeverity.Required) return null; // 카메라·적도의는 제거할 수 없다
        var choice = deviceId is null ? null : new RigChoice(deviceId, string.IsNullOrWhiteSpace(name) ? deviceId : name);

        if (options.Value.Simulate)
        {
            overrides.Set(kind, choice);
        }
        else
        {
            // 실기 미검증: connect?to=로 고르면 N.I.N.A.가 프로필에 저장한다. 제거는 연결을 끊고 프로필 값을 "없음"으로
            var written = choice is not null
                ? await nina.ConnectAsync(kind, choice.Id, ct)
                : await nina.DisconnectAsync(kind, ct)
                  && await nina.ChangeProfileValueAsync($"{slot.ProfileKey}-{(kind == "guider" ? "GuiderName" : "Id")}", kind == "guider" ? "No_Guider" : "No_Device", ct);
            if (written) overrides.Clear(kind);
            else overrides.Set(kind, choice); // N.I.N.A.에 쓰지 못해도 AA에서는 바로 반영
        }
        return await connector.DescribeAsync(kind, ct);
    }
}
