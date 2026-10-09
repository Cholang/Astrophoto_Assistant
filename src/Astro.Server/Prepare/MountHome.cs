using Astro.Nina;
using Microsoft.Extensions.Options;

namespace Astro.Server.Prepare;

/// <summary>
/// 진행 표시 아래 "적도의 홈" 버튼 (2026-10-09 사용자 요청): 언제든 적도의를 홈으로 보내고 추적을 끈다.
/// 움직이면 안 되는 때(촬영 중, 적도의를 쓰는 작업 중, 이미 움직이는 중, 적도의 미연결)는 이유와 함께 막는다.
/// Set Home은 쓰지 않는다 (Go Home만)
/// </summary>
public sealed class MountHome(PrepareMode mode, NinaApiClient nina, Flow.PrepareFlow flow, Shoot.ShootSession shoot, Flow.MountLock mount,
    Tasks.Wrap.IWrapDevices wrap, ILogger<MountHome> log)
{
    private volatile bool _homing;

    public sealed record State(bool Available, bool Homing, string? Reason);

    public async Task<State> StateAsync(CancellationToken ct)
    {
        if (_homing) return new(false, true, "적도의를 홈으로 보내는 중입니다");
        if (!mode.Simulate && !await nina.IsConnectedAsync("mount", ct)) return new(false, false, "적도의가 연결되지 않았습니다");
        if (shoot.Active) return new(false, false, "촬영 중에는 홈으로 보낼 수 없습니다. 촬영을 멈춘 뒤 눌러 주세요");
        foreach (var runner in new[] { flow.Rig, flow.Target, flow.Wrap })
            if (runner.ActiveTaskTitle is { } task) return new(false, false, $"{task} 작업 중에는 홈으로 보낼 수 없습니다");
        if (mount.IsHeld) return new(false, false, "적도의가 다른 작업에 쓰이는 중입니다");
        if (!mode.Simulate && await nina.GetInfoAsync("guider", ct) is { ValueKind: System.Text.Json.JsonValueKind.Object } g
            && g.TryGetProperty("State", out var st) && st.GetString() is { } state
            && (state.Contains("Guiding", StringComparison.OrdinalIgnoreCase) || state.Contains("Calibrating", StringComparison.OrdinalIgnoreCase)))
            return new(false, false, "가이딩 중에는 홈으로 보낼 수 없습니다. 가이딩이 멈춘 뒤 눌러 주세요");
        return new(true, false, null);
    }

    /// <summary>지금 보낼 수 있으면 시작하고 null, 아니면 이유. 홈·추적 끔은 뒤에서 (적도의 잠금 안에서)</summary>
    public async Task<string?> StartAsync(CancellationToken ct)
    {
        var s = await StateAsync(ct);
        if (!s.Available) return s.Reason;
        _homing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await using (await mount.AcquireAsync(CancellationToken.None))
                {
                    if (!await wrap.HomeAsync(CancellationToken.None)) log.LogWarning("홈 버튼: 홈으로 보내지 못함");
                    if (!await wrap.TrackingOffAsync(CancellationToken.None)) log.LogWarning("홈 버튼: 추적을 끄지 못함");
                }
            }
            catch (Exception e) { log.LogWarning(e, "홈 버튼 실패"); }
            finally { _homing = false; }
        });
        return null;
    }
}
