using Astro.Server.Prepare.Sim;

namespace Astro.Server.Prepare.Tasks.TestShot;

/// <summary>⑦ 모의 장비. "test.expose" = 노출 실패, "test.download" = 두 번 다 못 받음, "test.bright" = 달빛으로 배경이 밝음(노출을 줄이면 괜찮아짐)</summary>
public sealed class SimulatedTestShotDevices(SimOptions sim, SimFaults faults) : ITestShotDevices
{
    private bool _exposing, _saved, _failDownload, _bright;
    private int _lastExposure;

    public async Task<bool> ExposeAsync(int exposureSeconds, int iso, Action<int> remaining, CancellationToken ct)
    {
        _exposing = true;
        _saved = false;
        _lastExposure = exposureSeconds;
        if (faults.Take("test.expose")) { _exposing = false; return false; }
        if (faults.Take("test.download")) _failDownload = true;
        if (faults.Take("test.bright")) _bright = true;
        try
        {
            for (var s = exposureSeconds; s >= 0; s -= 8) { remaining(Math.Max(0, s)); await sim.Delay(70, ct); }
            return true;
        }
        finally { _exposing = false; }
    }

    private int _downloadTries;

    public async Task<ShotStats?> DownloadAndAnalyzeAsync(DateTimeOffset since, CancellationToken ct)
    {
        await sim.Delay(900, ct);
        if (_failDownload)
        {
            if (++_downloadTries >= 2) { _failDownload = false; _downloadTries = 0; }
            return null;
        }
        _saved = true;
        var bright = _bright && _lastExposure > 90;
        if (!bright) _bright = false;
        return new ShotStats(2.3, 0.3, bright ? 0.5 : 0.2, bright ? 2.8 : 0.4, 0.6, $"시험/test_{_lastExposure}s.raf");
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await sim.Delay(50, ct);
        if (faults.Take("stop.fail")) return false;
        _exposing = false;
        return true;
    }

    public Task<TestShotEndState> ReadEndStateAsync(CancellationToken ct) => Task.FromResult(new TestShotEndState(_exposing, true, _saved));
}
