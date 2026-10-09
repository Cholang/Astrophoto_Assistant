using System.Text.Json;
using Astro.Core;

namespace Astro.Server.Engine;

/// <summary>
/// 카메라 전원을 전원 허브의 어느 출력에서 받는가 (docs/PRECHECK_DESIGN.md J05, 2026-10-09 사용자 결정). null = 배터리.
/// 출력을 고를 때 "Empire에서 그 출력 전압을 카메라 어댑터에 맞췄다"는 확인을 받는다 — 아이라는 전압을 읽거나 바꾸지 못하고 켜고 끄기만 한다
/// (10/09 실기: 7~8.4V 어댑터를 12V 출력에 꽂아 켜지지 않음, 조절 출력 DC2를 8V로 맞추자 켜짐). 데이터 폴더의 camera-power.json
/// </summary>
public sealed class CameraPowerStore
{
    private readonly string _file;
    private readonly Lock _gate = new();
    private Saved _saved;

    private sealed record Saved(string? Outlet, DateTimeOffset? ConfirmedAt);

    public CameraPowerStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder);
        _file = Path.Combine(root, "camera-power.json");
        _saved = Load();
    }

    /// <summary>카메라 전원 출력 이름 (N.I.N.A. 스위치 이름 그대로, 예: "Regulated 0-13.2V adjustable DC2: DC2"). 배터리면 null</summary>
    public string? Outlet
    {
        get { lock (_gate) return _saved.Outlet; }
    }

    /// <summary>출력을 고른다 (전압을 맞췄다는 확인을 받은 뒤에만 부른다). null = 배터리</summary>
    public void Set(string? outlet)
    {
        lock (_gate)
        {
            _saved = new Saved(outlet, outlet is null ? null : DateTimeOffset.Now);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(_saved));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private Saved Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(_file)) ?? new(null, null) : new(null, null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(null, null); }
    }
}
