namespace Astro.Server.Prepare.Sim;

/// <summary>
/// 모의 장비 공통 (2026-10-01 결정 — 장비 없이 흐름·화면부터). 시간은 Speed로 줄이고(테스트는 0),
/// 화면·테스트용으로 다음 동작 하나를 실패시킬 수 있다 (Faults.Arm("focus.stars") 등).
/// </summary>
public sealed class SimOptions
{
    /// <summary>모의 시간 배율. 1 = 시안과 비슷한 속도, 0 = 기다리지 않음(테스트)</summary>
    public double Speed { get; set; } = 1;

    public Task Delay(int ms, CancellationToken ct) =>
        Speed <= 0 ? Task.CompletedTask : Task.Delay(TimeSpan.FromMilliseconds(ms * Speed), ct);
}

/// <summary>다음에 부르는 동작 하나를 실패시킨다. 키: "작업.과정" (예: "calibration.star", "stop.fail")</summary>
public sealed class SimFaults
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _armed = [];

    public static readonly IReadOnlyList<string> Known =
    [
        "polar.dialog", "polar.handover", "polar.stars", "polar.giveback",
        "calibration.star", "calibration.measure",
        "slew.move", "slew.low",
        "focus.stars", "focus.stall", "focus.temp",
        "center.solve", "center.hfr",
        "guiding.star", "guiding.calibration",
        "test.expose", "test.download", "test.bright",
        "shoot.trail", "shoot.cloud", "shoot.temp", "shoot.flip", "shoot.low",
        "shoot.light", "shoot.wind", "shoot.dew", "shoot.mount", "shoot.guider",
        "shoot.stopfail", "shoot.flipfail", "shoot.abortfail", "shoot.reselectfail", "shoot.movefail", "shoot.unstable", "shoot.unstablelong", "shoot.nostars", "shoot.flipmoving", "shoot.hotpixel", "wrap.tracking", "wrap.home", "wrap.dark",
        "wrap.bright",
        "stop.fail",
    ];

    public void Arm(string key)
    {
        lock (_gate) _armed.Add(key);
    }

    /// <summary>걸려 있으면 한 번만 true (꺼낸다)</summary>
    public bool Take(string key)
    {
        lock (_gate) return _armed.Remove(key);
    }

    public bool IsArmed(string key)
    {
        lock (_gate) return _armed.Contains(key);
    }
}
