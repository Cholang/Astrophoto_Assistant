using System.Text.Json;
using Astro.Server.Assistant;
using Astro.Server.Prepare.Flow;
using Astro.Server.Shoot;

namespace Astro.Server.Session;

/// <summary>
/// 그날 밤 진행 기록 (2026-10-08 사용자 결정). AA가 중간에 꺼져도(직접 종료·크래시·컴퓨터 꺼짐) 다시 켜면 알 수 있게 파일(session.json)로 남긴다.
/// 장비 준비 이후 단계(rig·plan·target·shoot·wrap)에서만 기록하고, 요약 화면에 닿으면 끝난 밤(Closed)으로 둔다.
/// </summary>
public sealed record SessionSnapshot(
    DateOnly Evening, string? ProfileId, string Phase, DateTimeOffset UpdatedAt, DateTimeOffset BootedAt, bool Closed,
    PreparationPlan? Plan, ShootingPlan? Talk, NightShootResult? Shots,
    PolarResult? Polar, CalibrationResult? Calibration, FocusResult? Focus, bool GuiderSkipped,
    int? FocusPosition, double? FocusTemperatureC, CalibrationResult? LastCalibration, DateTimeOffset? LastPolarAlignedAt);

/// <summary>
/// 다시 켰을 때 화면에 보일 것. Kind: resume = 이어서 할지 묻기, notice = 중단된 촬영이 있었다고 알리기만(컴퓨터가 꺼졌거나 오래 지남).
/// Phase = 이어서 하면 갈 화면
/// </summary>
public sealed record PendingSession(string Kind, string Summary, string? Phase);

public sealed class NightSessionStore
{
    /// <summary>이 시간 안에 다시 켰을 때만 이어서 할지 묻는다 (그보다 오래 지났으면 다시 온 것으로 보고 알리기만)</summary>
    public static readonly TimeSpan ResumeWithin = TimeSpan.FromHours(2);
    /// <summary>이보다 오래된 기록은 알리지도 않는다 (며칠 뒤 새 촬영에 지난 기록을 묻지 않게)</summary>
    public static readonly TimeSpan NoticeWithin = TimeSpan.FromHours(18);
    private static readonly string[] Active = ["rig", "plan", "target", "shoot", "wrap"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _file;
    private readonly Lock _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<DateTimeOffset> _bootedAt;
    private string _phase = "boot";
    private string? _profileId;

    public NightSessionStore(string? root = null, Func<DateTimeOffset>? now = null, Func<DateTimeOffset>? bootedAt = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Astro.Core.Product.DataFolder);
        _file = Path.Combine(root, "session.json");
        _now = now ?? (() => DateTimeOffset.Now);
        // 컴퓨터가 켜진 시각 — 기록할 때와 다르면 그 사이 컴퓨터가 꺼졌던 것 (장비 연결도 끊겼으므로 이어서 하지 않음)
        _bootedAt = bootedAt ?? (() => DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    /// <summary>화면이 지금 단계를 알린다. 요약에 닿으면 그 밤은 끝 (다시 켜도 묻지 않음)</summary>
    public void SetPhase(string phase, string? profileId)
    {
        lock (_gate)
        {
            _phase = phase;
            if (profileId is not null) _profileId = profileId;
        }
    }

    public bool Recording { get { lock (_gate) return Active.Contains(_phase) || _phase == "summary"; } }

    /// <summary>지금 상태를 기록한다 (진행 중 단계에서만 — 요약이면 끝난 밤으로)</summary>
    public void Save(PlanAssistant planner, PrepResults results, IPrepMemory memory)
    {
        string phase;
        string? profile;
        lock (_gate) { phase = _phase; profile = _profileId; }
        if (!Active.Contains(phase) && phase != "summary") return;
        var snap = new SessionSnapshot(
            TonightServiceEvening(), profile, phase, _now(), _bootedAt(), Closed: phase == "summary",
            planner.Confirmed, planner.Plan, results.Get<NightShootResult>(),
            results.Get<PolarResult>(), results.Get<CalibrationResult>(), results.Get<FocusResult>(), results.Get<GuiderSkipped>() is not null,
            memory.LastFocus?.Position, memory.LastFocus?.TemperatureC, memory.LastCalibration, memory.LastPolarAlignedAt);
        Write(snap);
    }

    private DateOnly TonightServiceEvening() => Sky.TonightService.EveningOf(_now());

    public SessionSnapshot? Read()
    {
        lock (_gate)
        {
            try { return File.Exists(_file) ? JsonSerializer.Deserialize<SessionSnapshot>(File.ReadAllText(_file), Json) : null; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return null; }
        }
    }

    private void Write(SessionSnapshot snap)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(snap, Json));
                File.Move(tmp, _file, overwrite: true); // 꺼지는 순간에도 반쯤 쓴 파일이 남지 않게
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* 다음 기록 때 다시 */ }
        }
    }

    /// <summary>
    /// 다시 켰을 때 물을 것. 이어서(resume): 같은 밤 + 같은 프로필 + 2시간 안 + 그 사이 컴퓨터가 꺼지지 않음 + 계획 끝 시각 전.
    /// 알리기만(notice): 그 밖에 18시간 안의 끝나지 않은 기록. 아니면 null
    /// </summary>
    public PendingSession? Pending(string? profileId)
    {
        if (Read() is not { Closed: false } s) return null;
        var now = _now();
        var age = now - s.UpdatedAt;
        if (age > NoticeWithin || age < TimeSpan.Zero) return null;
        var rebooted = (_bootedAt() - s.BootedAt).Duration() > TimeSpan.FromMinutes(2);
        var resumable = !rebooted && age <= ResumeWithin && s.Evening == TonightServiceEvening()
            && (profileId is null || s.ProfileId is null || s.ProfileId == profileId)
            && (s.Plan is null || now < s.Plan.End);
        var what = Describe(s);
        if (resumable) return new PendingSession("resume", $"{what} · {s.UpdatedAt:HH:mm}에 멈췄어요", Destination(s));
        var why = rebooted ? "컴퓨터가 꺼져 이어서 할 수 없어요" : "시간이 많이 지나 새로 시작해요";
        return new PendingSession("notice", $"지난번 촬영이 중단됐어요 · {what} · {s.UpdatedAt:M월 d일 HH:mm} · {why}", null);
    }

    /// <summary>이어서 하면 갈 화면: 촬영·대상 중이었으면 대상(적도의 위치를 모르니 이동부터), 그 밖에는 그 단계</summary>
    private static string Destination(SessionSnapshot s) => s.Phase switch
    {
        "shoot" or "target" => s.Plan is null ? "plan" : "target",
        "plan" => "plan",
        "wrap" => "wrap",
        _ => "rig",
    };

    private static string Describe(SessionSnapshot s)
    {
        var name = s.Plan is { } p ? p.TargetKoreanName is { } k ? $"{p.TargetName} {k}" : p.TargetName : null;
        var shot = s.Plan is { } plan && s.Shots?.Targets.LastOrDefault(t => t.PlanKey == PlanKeyOf(plan)) is { } t ? t : null;
        return s.Phase switch
        {
            "shoot" when name is not null => $"{name} 촬영 {shot?.Good ?? 0}/{s.Plan!.EstimatedFrames}장",
            "target" when name is not null => $"{name} 대상 맞추는 중",
            "plan" => "촬영 계획 중",
            "wrap" => "마무리 중",
            _ => "장비 준비 중",
        };
    }

    /// <summary>촬영 기록 한 줄을 그 계획과 짝짓는 열쇠 (ShootSession과 같음)</summary>
    public static string PlanKeyOf(PreparationPlan plan) => plan.ConfirmedAt.ToString("O");

    /// <summary>이어서: 그날 밤 결과 기록·기억·계획을 되살린다</summary>
    public async Task<string?> ResumeAsync(PlanAssistant planner, PrepResults results, IPrepMemory memory, CancellationToken ct)
    {
        if (Read() is not { } s) return null;
        if (s.Shots is { } shots) results.Set(shots);
        if (s.Polar is { } polar) results.Set(polar);
        if (s.Calibration is { } cal) results.Set(cal);
        if (s.Focus is { } focus) results.Set(focus);
        if (s.GuiderSkipped) results.Set(new GuiderSkipped("이어서 — 전에 가이딩 없이 진행", _now()));
        if (s.FocusPosition is { } pos) memory.LastFocus = (pos, s.FocusTemperatureC);
        memory.LastCalibration = s.LastCalibration;
        memory.LastPolarAlignedAt = s.LastPolarAlignedAt;
        if (s.Plan is { } plan) await planner.RestoreAsync(plan, s.Talk, ct);
        return Destination(s);
    }

    /// <summary>새로 시작: 기록은 남기되(요약용) 다시 묻지 않는다</summary>
    public void Dismiss()
    {
        if (Read() is { Closed: false } s) Write(s with { Closed = true });
    }
}

/// <summary>진행 중인 동안 20초마다 기록한다 (갑자기 꺼져도 마지막 상태가 남게)</summary>
public sealed class NightSessionRecorder(NightSessionStore store, PlanAssistant planner, PrepareFlow flow, IPrepMemory memory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            if (store.Recording) store.Save(planner, flow.ResultsFor(Sky.TonightService.EveningOf(DateTimeOffset.Now)), memory);
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
