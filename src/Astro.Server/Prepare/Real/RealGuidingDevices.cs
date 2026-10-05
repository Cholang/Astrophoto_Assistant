using System.Text.Json;
using Astro.Server.Prepare.Tasks.Guiding;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ⑥ 가이딩 실장비: 시작·중지는 N.I.N.A. 가이더 명령(시퀀스의 디더링·반전이 상태를 알도록), 측정값은 PHD2 이벤트(GuideStep).
/// 2026-10-06 실기: N.I.N.A. guider/start는 별이 없어도 2분쯤 뒤 "Guiding started"로 답한다(PHD2는 Looping) → 성공 여부는 PHD2 상태로.
/// guider/stop은 가이딩만 멈추고 PHD2는 Looping으로 남는다 → 멈춤에는 PHD2 stop_capture도.
/// </summary>
public sealed class RealGuidingDevices(NinaRig rig, Phd2Client phd2) : IGuidingDevices
{
    private const int Samples = 30;

    public async Task<GuideStart> StartAsync(double exposureSeconds, Action<string> phase, CancellationToken ct)
    {
        try { await phd2.CallAsync("set_exposure", new object[] { (int)(exposureSeconds * 1000) }, ct); }
        catch (Phd2Exception e) { return new GuideStart(false, false, Problem: e.Message); }

        var start = rig.StartGuidingAsync(calibrate: false, ct);
        // N.I.N.A.가 답하기 전에도 PHD2가 가이딩에 들어가면 안정화 단계로
        var settling = false;
        while (!start.IsCompleted)
        {
            await Task.WhenAny(start, Task.Delay(1000, ct));
            if (!settling && await StateAsync(ct) == "Guiding") { settling = true; phase("settle"); }
        }
        var reply = await start;
        if (!reply.Ok) return new GuideStart(false, false, Problem: reply.Error ?? "가이딩을 시작하지 못했습니다");
        var state = await StateAsync(ct);
        if (state != "Guiding")
        {
            // 별을 못 찾으면 PHD2는 Looping으로 남는다 → 다음 시도 전에 멈춰 둔다
            await StopPhd2Async(ct);
            return new GuideStart(true, false, Problem: "가이드 별을 찾지 못했습니다");
        }
        if (!settling) phase("settle");
        await Task.Delay(TimeSpan.FromSeconds(10), ct); // 안정화 (PHD2 settle 기본 10초 안)
        return new GuideStart(true, true);
    }

    public async Task<GuideStats> MeasureAsync(Action<double, double> sample, CancellationToken ct)
    {
        using var events = await phd2.SubscribeAsync(ct);
        double scale;
        try { scale = (await phd2.CallAsync("get_pixel_scale", ct: ct)).GetDouble(); }
        catch (Exception e) when (e is Phd2Exception or InvalidOperationException) { scale = 1; }
        double sra = 0, sde = 0;
        int n = 0, lost = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        while (n < Samples && DateTime.UtcNow < deadline)
        {
            if (await events.WaitAsync(["GuideStep", "StarLost"], deadline - DateTime.UtcNow, ct) is not { } e) break;
            if (Phd2Events.Name(e) == "StarLost") { lost++; continue; }
            var ra = Num(e, "RADistanceRaw") * scale;
            var de = Num(e, "DECDistanceRaw") * scale;
            sra += ra * ra;
            sde += de * de;
            n++;
            sample(ra, de);
        }
        return n == 0 ? new GuideStats(double.NaN, double.NaN, lost) : new GuideStats(Math.Sqrt(sra / n), Math.Sqrt(sde / n), lost);
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await rig.StopGuidingAsync(ct);
        return await StopPhd2Async(ct);
    }

    public async Task<GuidingEndState> ReadEndStateAsync(CancellationToken ct)
    {
        var s = await StateAsync(ct);
        return new GuidingEndState(s == "Guiding", s == "Guiding", s == "LostLock" ? 1 : 0);
    }

    private async Task<string> StateAsync(CancellationToken ct)
    {
        try { return await phd2.AppStateAsync(ct); } catch (Phd2Exception) { return ""; }
    }

    private async Task<bool> StopPhd2Async(CancellationToken ct)
    {
        try
        {
            await phd2.CallAsync("stop_capture", ct: ct);
            for (var i = 0; i < 30; i++)
            {
                if (await phd2.AppStateAsync(ct) == "Stopped") return true;
                await Task.Delay(200, ct);
            }
        }
        catch (Phd2Exception) { }
        return false;
    }

    private static double Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
