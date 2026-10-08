using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Astro.Server.Engine;

/// <summary>
/// 가이더 연결 확인 (2026-10-06 사용자 결정). N.I.N.A.의 "가이더 연결됨"은 PHD2 프로그램에 붙었다는 뜻일 뿐,
/// PHD2 안의 카메라·적도의가 연결됐는지는 모른다 → PHD2에 직접 물어본다 (JSON-RPC, 기본 포트 4400).
/// 실기 사례: N.I.N.A. 알림은 "가이더가 연결됨"인데 PHD2는 카메라 연결 실패(USB 포트를 바꿔 다시 골라야 함), 적도의는 시뮬레이터였다.
/// </summary>
public static class Phd2Check
{
    public sealed record Problem(string Message, string Fix);

    /// <summary>문제가 없거나 PHD2에 직접 물을 수 없으면 null (물을 수 없을 때는 막지 않는다 — N.I.N.A.는 붙었으므로)</summary>
    public static async Task<Problem?> CheckAsync(string host, int port, string? ninaMountName, CancellationToken ct)
    {
        JsonElement? equipment;
        try
        {
            equipment = await CallAsync(host, port, "get_current_equipment", ct);
        }
        catch (Exception e) when (e is SocketException or IOException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        if (equipment is not { ValueKind: JsonValueKind.Object } eq) return null;

        var (cameraName, cameraOn) = Part(eq, "camera");
        var (mountName, mountOn) = Part(eq, "mount");
        if (cameraName is null || !cameraOn)
            return new Problem("PHD2에서 가이드 카메라가 연결되지 않았습니다",
                "PHD2의 장비 연결 창에서 가이드 카메라를 다시 고르고(USB 포트를 바꾸면 다시 골라야 합니다) 연결한 뒤, 다시 연결을 눌러 주세요.");
        // 가이드 카메라가 PHD2 내장 시뮬레이터인데 N.I.N.A. 적도의는 실제 (2026-10-08 실기: "newbee" 프로필 카메라가 Simulator로 남아 연결은 통과했지만 SharpCap이 카메라를 못 엶)
        if (IsSimulator(cameraName) && !(ninaMountName is { Length: > 0 } nmc && IsSimulator(nmc)))
            return new Problem($"PHD2의 가이드 카메라가 시뮬레이터입니다 (PHD2: {cameraName})",
                "PHD2의 장비 연결 창에서 카메라를 실제 가이드 카메라로 바꿔 연결하고, 창을 닫은 뒤 다시 연결을 눌러 주세요.");
        if (mountName is null || !mountOn)
            return new Problem("PHD2에서 적도의가 연결되지 않았습니다",
                "PHD2의 장비 연결 창에서 적도의를 연결한 뒤 다시 연결을 눌러 주세요. 가이딩 보정은 이 적도의로 보냅니다.");
        // 시뮬레이터: N.I.N.A. 적도의도 시뮬레이터면(장비 없이 시험) 같은 것으로 본다 — 이름은 서로 다를 수 있음
        // (2026-10-08: N.I.N.A. "Telescope Simulator for .NET", PHD2 "Alpaca Telescope Simulator (ASCOM)")
        var phdSim = IsSimulator(mountName);
        var ninaSim = ninaMountName is { Length: > 0 } ns && IsSimulator(ns);
        if (phdSim != ninaSim && !IsOnCamera(mountName)
            || !phdSim && !ninaSim && ninaMountName is { Length: > 0 } nm && !mountName.Contains(nm, StringComparison.OrdinalIgnoreCase) && !IsOnCamera(mountName))
            return new Problem($"PHD2의 적도의가 N.I.N.A.와 다릅니다 (PHD2: {mountName})",
                $"PHD2의 장비 연결 창에서 적도의를 {(ninaMountName is { Length: > 0 } n ? $"{n}(으)로" : "실제 적도의로")} 바꾼 뒤 다시 연결을 눌러 주세요. 지금은 가이딩 보정이 실제 적도의로 가지 않습니다.");
        return null;
    }

    /// <summary>
    /// 카메라의 ST-4 포트로 보정을 보내는 방식 — 적도의 이름이 N.I.N.A.와 달라도 맞는 설정.
    /// PHD2는 "On Camera"(띄어쓰기)로 알려 준다(2026-10-08 확인) — 메뉴 이름은 "On-camera"라 띄어쓰기·하이픈을 무시하고 본다
    /// </summary>
    internal static bool IsOnCamera(string name)
    {
        var n = name.Replace(" ", "").Replace("-", "");
        return n.Contains("OnCamera", StringComparison.OrdinalIgnoreCase) || n.Contains("ST4", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSimulator(string name) => name.Contains("Simulator", StringComparison.OrdinalIgnoreCase);

    private static (string? Name, bool Connected) Part(JsonElement eq, string key) =>
        eq.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Object
            ? (p.TryGetProperty("name", out var n) ? n.GetString() : null, p.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True)
            : (null, false);

    /// <summary>요청 하나 보내고 같은 id의 답을 기다린다. PHD2는 이벤트(Version·AppState …)를 먼저 줄줄이 보내므로 건너뛴다</summary>
    private static async Task<JsonElement?> CallAsync(string host, int port, string method, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token);
        await using var stream = client.GetStream();
        var request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { method, id = 1 }) + "\r\n");
        await stream.WriteAsync(request, cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != 1) continue;
            return root.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        return null;
    }
}
