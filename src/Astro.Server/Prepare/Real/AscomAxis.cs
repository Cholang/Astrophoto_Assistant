namespace Astro.Server.Prepare.Real;

/// <summary>
/// RA축만 돌리기 (① 극 찾기 — SharpCap은 안내만 하고 직접 돌리지 않음). goto는 극 근처에서 Dec축까지 움직여서 ASCOM MoveAxis를 직접 쓴다.
/// OnStep 드라이버는 로컬 서버라 N.I.N.A.와 동시에 연결해도 된다 (2026-10-04 실기, 최대 3.55°/s).
/// 주의: 이 적도의는 홈에서 한쪽으로 약 60°까지만 돈다(원인 미해결 — 다음에 확인). 그래서 회전은 60°로 하고 홈 근처에서 시작한다.
/// 회전 방향(부호)은 실기에서 아직 확인하지 않았다.
/// </summary>
public sealed class AscomAxis(ILogger<AscomAxis> log)
{
    public string DriverId { get; set; } = "ASCOM.OnStep.Telescope";

    /// <summary>RA축을 rate(°/s)로 seconds 동안 돌리고 멈춘다. 진행 각도를 알린다. 실패면 이유</summary>
    public async Task<string?> RotateRaAsync(double rate, double seconds, Action<double> progress, CancellationToken ct)
    {
        dynamic? scope = null;
        try
        {
            scope = Create();
            if (!(bool)scope.Connected) scope.Connected = true;
            if (!(bool)scope.CanMoveAxis(0)) return "적도의가 축 돌리기를 지원하지 않습니다";
            scope.MoveAxis(0, rate);
            var t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalSeconds < seconds)
            {
                progress(Math.Abs(rate) * (DateTime.UtcNow - t0).TotalSeconds);
                await Task.Delay(250, ct);
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            log.LogWarning(e, "RA 회전 실패");
            return $"적도의 축을 돌리지 못했습니다 ({e.Message})";
        }
        finally
        {
            // 취소·오류여도 반드시 멈춤 명령
            try { scope?.MoveAxis(0, 0.0); } catch (Exception e) { log.LogWarning(e, "RA 회전 멈춤 실패"); }
        }
    }

    /// <summary>축 돌리기 멈춤 (멈춤 확인은 NinaRig.ConfirmStillAsync)</summary>
    public bool Stop()
    {
        try
        {
            dynamic scope = Create();
            if ((bool)scope.Connected) scope.MoveAxis(0, 0.0);
            return true;
        }
        catch (Exception e)
        {
            log.LogWarning(e, "RA 축 멈춤 실패");
            return false;
        }
    }

    private dynamic Create()
    {
        var type = Type.GetTypeFromProgID(DriverId) ?? throw new InvalidOperationException($"ASCOM 드라이버 {DriverId}를 찾지 못했습니다");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("ASCOM 드라이버를 만들지 못했습니다");
    }
}
