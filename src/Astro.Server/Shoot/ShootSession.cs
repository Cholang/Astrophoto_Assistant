using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Astro.Server.Prepare.Flow;

namespace Astro.Server.Shoot;

/// <summary>그날 밤 대상 하나의 촬영 기록 (마무리의 다크·요약이 읽는다)</summary>
/// <summary>PausedMinutes = 원인별 멈춘 시간(분): cloud · light · jump · guider …</summary>
public sealed record TargetShots(string TargetId, string Name, int Good, int Excluded, int ExposureSeconds, int Iso, IReadOnlyDictionary<string, int> Tally, string? Folder, IReadOnlyDictionary<string, double>? PausedMinutes = null);

/// <summary>그날 밤 찍은 대상들 (결과 기록 — 촬영이 갱신)</summary>
public sealed record NightShootResult(IReadOnlyList<TargetShots> Targets);

/// <summary>촬영 화면 상태. 화면은 이것만 보고 그린다</summary>
public sealed record ShootView(
    bool Started, string? Target, int Planned, int Good, int Excluded, int ExposureSeconds, int Iso, int Elapsed,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShootMode>))] ShootMode Mode,
    int FlipStep, DateTimeOffset? FlipAt, int? FlipInMinutes, IReadOnlyList<double> Guide, IReadOnlyList<double> Hfr, double FocusHfr,
    double PixelScale, string? LastGrade, IReadOnlyDictionary<string, int> Tally, IReadOnlyDictionary<string, string> Criteria,
    StatusLine? Note, IReadOnlyList<object>? FocusPoints, string? Ended, DateTimeOffset? EndAt, string? PhotoUrl, DateTimeOffset? PhotoAt,
    int Version, bool Simulated, string? Pause = null, int PausedSeconds = 0, int GuideExposureMs = 0);

/// <summary>Paused = 가이드 별을 잃어 멈춤 (원인은 ShootView.Pause: light · cloud · jump · guider)</summary>
public enum ShootMode { Idle, Shoot, Dither, Flip, Focus, Paused, Finishing, Ended }

/// <summary>
/// 촬영 (DESIGN.md "촬영", 시안 mockups/aa-shoot-wrap-v10.html). 확정 계획·대상 묶음의 결과로 찍는다.
/// 사진마다 등급(ShotGrader) → F면 제외 폴더로. 디더링(3장마다)·자오선 반전·초점 다시 맞추기·구름 멈춤은 묻지 않고 자동.
/// 끝: 계획 장수(쓸 사진 기준) · 대상이 30° 아래 · 계획 끝 시각(새벽) — 또는 사용자의 "촬영 중단"(지금 사진까지 찍고).
/// 끝나면 가이딩을 멈춘다. 그 뒤(마무리 / 다른 대상)는 화면이 고른다.
/// </summary>
public sealed class ShootSession(IShootDevices devices, Prepare.PrepareMode mode, ILogger<ShootSession> log)
{
    public const int DitherEvery = 3;
    public const double FlipAfterDeg = 1.25;        // 자오선 지나 5분 뒤 반전
    public const double RefocusTempC = 2;
    public const double RefocusHfrFactor = 1.3;
    public const double MinAltitude = 30;
    private const int Spark = 48;

    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _version;
    private CancellationTokenSource? _cts;
    private Task _run = Task.CompletedTask;
    private PrepContext? _ctx;
    private string? _key;

    // 화면 상태
    private ShootMode _mode = ShootMode.Idle;
    private int _good, _excluded, _elapsed, _flipStep;
    private readonly List<double> _guide = [], _hfr = [];
    private readonly Dictionary<string, int> _tally = ShotGrader.Letters.ToDictionary(l => l, _ => 0);
    private string? _lastGrade, _ended, _folder;
    private StatusLine? _note;
    private DateTimeOffset _noteUntil;
    private List<object>? _focusPoints;
    private DateTimeOffset? _photoAt;
    private double _focusHfr = 2;
    private volatile bool _stopRequested;
    // 가이드 별 잃음 (GuideWatch)
    private string? _pause;
    private DateTimeOffset _pausedSince;
    private readonly Dictionary<string, double> _pausedMinutes = [];
    private readonly List<double> _snrSamples = [], _hfdSamples = [];
    private double _snrBaseline, _hfdBaseline;
    private double? _mainStarsRatio, _mainMeanRatio;
    private readonly List<DateTimeOffset> _jumps = [];
    private readonly Dictionary<GuideLoss, DateTimeOffset> _lastDegrade = [];
    private bool _windWarned;

    public static readonly int[] GuideExposuresMs = [1000, 1500, 2000, 2500, 3000, 3500, 4000];
    public static readonly TimeSpan LightWait = TimeSpan.FromMinutes(2), CloudWait = TimeSpan.FromMinutes(30), RecenterAfter = TimeSpan.FromMinutes(10);

    public PrepContext? Context { get { lock (_gate) return _ctx; } }

    public ShootView View()
    {
        lock (_gate) return Snapshot();
    }

    private ShootView Snapshot()
    {
        var c = _ctx;
        if (_note is not null && DateTimeOffset.Now > _noteUntil) _note = null;
        DateTimeOffset? flipAt = null, endAt = null;
        if (c is not null && _mode is not ShootMode.Ended and not ShootMode.Idle)
        {
            var ha = devices.HourAngleDeg(c);
            if (ha < FlipAfterDeg && !_flipped) flipAt = c.Now().AddMinutes((FlipAfterDeg - ha) / 15 * 60);
            var left = Math.Max(0, c.Plan.EstimatedFrames - _good);
            var est = c.Now().AddSeconds(left * (c.ExposureSeconds + 8.0));
            endAt = est < c.Plan.End ? est : c.Plan.End;
        }
        return new ShootView(c is not null, c is null ? null : c.TargetName, c?.Plan.EstimatedFrames ?? 0, _good, _excluded,
            c?.ExposureSeconds ?? 0, c is null ? 0 : c.Plan.Iso, _elapsed, _mode, _flipStep, flipAt,
            flipAt is { } fa && c is not null ? Math.Max(1, (int)Math.Ceiling((fa - c.Now()).TotalMinutes)) : null, _guide.ToArray(), _hfr.ToArray(), _focusHfr,
            c?.MainPixelScaleArcsec ?? 0, _lastGrade, new Dictionary<string, int>(_tally), ShotGrader.Criteria, _note, _focusPoints?.ToArray(),
            _ended, endAt, devices.PhotoUrl, _photoAt, _version, mode.Simulate,
            _pause, _pause is null ? 0 : (int)(DateTimeOffset.Now - _pausedSince).TotalSeconds, devices.GuideExposureMs);
    }

    public async IAsyncEnumerable<ShootView> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var last = -1;
        while (!ct.IsCancellationRequested)
        {
            ShootView view;
            Task changed;
            lock (_gate)
            {
                view = Snapshot();
                changed = _changed.Task;
            }
            if (view.Version != last) { last = view.Version; yield return view; }
            try { await changed.WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch (TimeoutException) { /* 알림이 끝났는지 다시 그리기 */ last = -1; }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task NextChangeAsync()
    {
        lock (_gate) return _changed.Task;
    }

    private void Changed()
    {
        _version++;
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    private void Set(Action change)
    {
        lock (_gate)
        {
            change();
            if (_modes.Count == 0 || _modes[^1] != _mode) _modes.Add(_mode);
            Changed();
        }
    }

    private readonly List<ShootMode> _modes = [];

    /// <summary>[테스트] 지나온 상태들</summary>
    internal IReadOnlyList<ShootMode> ModeHistory { get { lock (_gate) return _modes.ToArray(); } }

    private void Note(string text, Tone tone, double seconds = 6) =>
        Set(() => { _note = new StatusLine(text, tone); _noteUntil = DateTimeOffset.Now.AddSeconds(seconds); });

    // ── 시작 · 중단 ─────────

    /// <summary>대상 묶음이 끝난 상황(계획·결과)으로 촬영을 시작한다. 같은 계획이 찍는 중이면 그대로(화면 재접속)</summary>
    public void Start(PrepContext ctx)
    {
        var key = ctx.Plan.ConfirmedAt.ToString("O");
        lock (_gate)
        {
            if (_key == key && _mode is not ShootMode.Ended) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ctx = ctx;
            _key = key;
            _good = _excluded = _elapsed = _flipStep = 0;
            _guide.Clear(); _hfr.Clear();
            foreach (var l in ShotGrader.Letters) _tally[l] = 0;
            _lastGrade = _ended = _folder = null; _note = null; _focusPoints = null; _photoAt = null;
            _flipped = false; _stopRequested = false; _recordIndex = -1; _hfrRecent.Clear();
            _pause = null; _pausedMinutes.Clear(); _snrSamples.Clear(); _hfdSamples.Clear(); _snrBaseline = _hfdBaseline = 0;
            _mainStarsRatio = _mainMeanRatio = null; _jumps.Clear(); _lastDegrade.Clear(); _windWarned = false;
            _focusHfr = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, Hfr: { } rh } ? rh : ctx.Results.Get<FocusResult>()?.Hfr ?? 2;
            _mode = ShootMode.Shoot;
            Changed();
        }
        var ct = _cts.Token;
        log.LogInformation("촬영 시작: {Target}", ctx.TargetName);
        _run = Task.Run(() => RunAsync(ctx, ct), CancellationToken.None);
    }

    /// <summary>"촬영 중단": 지금 사진은 끝까지 찍고 멈춘다. 찍고 있지 않으면 이유</summary>
    public string? Stop()
    {
        lock (_gate)
        {
            if (_ctx is null || _mode is ShootMode.Ended or ShootMode.Idle) return "찍고 있지 않습니다.";
            _stopRequested = true;
            _note = new StatusLine("지금 사진까지 찍고 멈춥니다", Tone.Busy);
            _noteUntil = DateTimeOffset.MaxValue;
            Changed();
        }
        return null;
    }

    private bool _flipped;
    private double? _lastFocusTemp;

    private async Task RunAsync(PrepContext ctx, CancellationToken ct)
    {
        _lastFocusTemp = ctx.Results.Get<FocusCheckResult>() is { Refocused: true, TemperatureC: { } t } ? t : ctx.Results.Get<FocusResult>()?.TemperatureC;
        int? baseStars = null; double? baseMean = null;
        var firstStars = new List<int>(); var firstMeans = new List<double>();
        var fails = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                // 끝 조건
                if (_stopRequested) { await EndAsync("user", ct); return; }
                if (_good >= ctx.Plan.EstimatedFrames) { await EndAsync("done", ct); return; }
                if (!ctx.IgnoreAltitude && devices.TargetAltitude(ctx) < MinAltitude) { await EndAsync("low", ct); return; }
                if (ctx.Now() >= ctx.Plan.End) { await EndAsync("dawn", ct); return; }

                // 자오선 반전 (항상 자동, 한 번)
                if (!_flipped && devices.HourAngleDeg(ctx) > FlipAfterDeg)
                {
                    Set(() => { _mode = ShootMode.Flip; _flipStep = 0; });
                    var ok = await devices.FlipAsync(ctx, s => Set(() => _flipStep = s), ct);
                    _flipped = true;
                    Set(() => _mode = ShootMode.Shoot);
                    Note(ok ? "자오선 반전을 마쳤어요 · 다시 찍어요" : "자오선 반전 뒤 가운데 맞추기가 안 됐어요 · 그대로 이어서 찍어요", ok ? Tone.Ok : Tone.Warn);
                }

                // 초점 다시 맞추기: 기온 2°C 이상, 또는 최근 좋은 사진의 별 크기가 초점 때보다 30% 넘게 큼
                var temp = await devices.TemperatureAsync(ct);
                var recent = _hfrRecent.Count >= 3 ? _hfrRecent.TakeLast(3).Average() : (double?)null;
                var tempDrift = temp is { } tn && _lastFocusTemp is { } tf && Math.Abs(tn - tf) >= RefocusTempC;
                if (tempDrift || recent > _focusHfr * RefocusHfrFactor)
                {
                    var why = tempDrift ? $"초점을 맞춘 뒤 기온이 {Math.Abs(temp!.Value - _lastFocusTemp!.Value):F1}°C 바뀌었어요" : "최근 사진의 별이 커졌어요";
                    var points = new List<object>();
                    Set(() => { _mode = ShootMode.Focus; _focusPoints = []; _note = new StatusLine(why, Tone.Warn); _noteUntil = DateTimeOffset.MaxValue; });
                    var r = await devices.RefocusAsync((p, h) => { points.Add(new { pos = p, hfr = h }); Set(() => _focusPoints = [.. points]); }, ct);
                    _lastFocusTemp = temp;
                    _hfrRecent.Clear();
                    Set(() => { _mode = ShootMode.Shoot; _focusPoints = null; _note = null; if (r.Ok) _focusHfr = r.Hfr; });
                    Note(r.Ok ? $"초점을 다시 맞췄어요 · 별 크기 {r.Hfr:F1} · 위치 {r.Position:N0}" : $"초점을 다시 맞추지 못했어요 · 그대로 찍어요 ({r.Problem})", r.Ok ? Tone.Ok : Tone.Warn);
                }

                // 가이드 별: 찍기 전에 확인 — 잃었으면 원인별로 (docs/SHOOT_IMPLEMENTATION.md B)
                var before = await CheckGuideAsync(ct);
                if (before.IsLost())
                {
                    if (!await HandleLossAsync(ctx, before, ct)) return;
                    continue;
                }

                // 한 장 (찍는 동안에도 가이드 별을 지켜봄 — 오래 잃으면 이 장을 멈추고 버림)
                Set(() => { _mode = _stopRequested ? ShootMode.Finishing : ShootMode.Shoot; _elapsed = 0; });
                var (shot, lost) = await ExposeWatchedAsync(ctx, ct);
                if (lost != GuideLoss.None)
                {
                    if (!await HandleLossAsync(ctx, lost, ct)) return;
                    continue;
                }
                if (!shot.Ok || shot.Stats is not { } st)
                {
                    fails++;
                    Note($"사진을 받지 못했어요 · 다시 찍어요 ({shot.Problem})", Tone.Warn);
                    if (fails >= 3) { await EndAsync("error", ct); return; }
                    continue;
                }
                fails = 0;
                var rms = st.GuideRmsArcsec ?? await devices.GuideRmsAsync(ct);
                var stats = st with { GuideRmsArcsec = rms };
                var grade = ShotGrader.Judge(stats, new GradeBaseline(_focusHfr, baseStars, baseMean, ctx.MainPixelScaleArcsec));
                var file = shot.File;
                if (grade.Excluded && file is not null) file = devices.MoveToExcluded(file) ?? file;
                if (!grade.Excluded)
                {
                    // 그날 밤 기준: 처음 좋은 사진 세 장의 별 수·배경
                    if (baseStars is null) { firstStars.Add(stats.Stars); if (stats.Mean is { } m) firstMeans.Add(m); }
                    if (baseStars is null && firstStars.Count >= 3) { baseStars = Median(firstStars); baseMean = firstMeans.Count >= 3 ? firstMeans.Order().ElementAt(1) : null; }
                    _hfrRecent.Add(stats.Hfr);
                }
                // 가이드 별 원인 판정에 쓰는 주 사진 신호 (그날 기준 대비)
                _mainStarsRatio = baseStars is > 0 ? stats.Stars / (double)baseStars : null;
                _mainMeanRatio = baseMean is > 0 && stats.Mean is { } mm ? mm / baseMean : null;
                Set(() =>
                {
                    if (grade.Excluded) _excluded++; else _good++;
                    _tally[grade.Letter]++;
                    _lastGrade = grade.Letter;
                    if (!grade.Excluded) { _hfr.Add(stats.Hfr); if (_hfr.Count > 20) _hfr.RemoveAt(0); }
                    if (shot.File is { } f) _folder ??= Path.GetDirectoryName(f);
                    _photoAt = DateTimeOffset.Now;
                });
                if (grade.Excluded) Note($"{_good + _excluded}번 사진: {grade.Reason} · 제외 폴더로 옮겼어요", Tone.Warn, 8);
                Record(ctx);

                // 디더링 (좋은 사진 3장마다)
                if (!grade.Excluded && _good % DitherEvery == 0 && !_stopRequested)
                {
                    Set(() => _mode = ShootMode.Dither);
                    await devices.DitherAsync(ct);
                    Set(() => _mode = ShootMode.Shoot);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e)
        {
            log.LogError(e, "촬영 오류");
            Note("예상하지 못한 오류로 촬영을 멈췄습니다", Tone.Fail, 600);
            await EndAsync("error", CancellationToken.None);
        }
    }

    private readonly List<double> _hfrRecent = [];

    // ── 가이드 별 지켜보기 (docs/SHOOT_IMPLEMENTATION.md "B. 가이딩 중 가이드 별을 잃었을 때") ─────────

    private int PollMs => mode.Simulate ? 300 : 3000;

    /// <summary>장비 신호를 읽어 원인을 가린다. 가이딩이 안정된 동안의 SNR·HFD로 기준을 잡고, 약해지면(이슬·가이드 초점·옅은 구름) 가이드 노출을 올리고 알린다</summary>
    private async Task<GuideLoss> CheckGuideAsync(CancellationToken ct)
    {
        GuideRaw raw;
        try { raw = await devices.GuideRawAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "가이딩 신호를 읽지 못함"); return GuideLoss.None; }
        if (raw.Guiding && raw.Snr.Count > 0)
        {
            // 처음 안정된 가이딩 20걸음의 중앙값이 그날 기준
            if (_snrBaseline <= 0) { _snrSamples.AddRange(raw.Snr.TakeLast(5)); if (_snrSamples.Count >= 20) _snrBaseline = _snrSamples.Order().ElementAt(_snrSamples.Count / 2); }
            if (_hfdBaseline <= 0 && raw.Hfd.Count > 0) { _hfdSamples.AddRange(raw.Hfd.TakeLast(5)); if (_hfdSamples.Count >= 20) _hfdBaseline = _hfdSamples.Order().ElementAt(_hfdSamples.Count / 2); }
        }
        var loss = GuideWatch.Classify(new GuideSignals(raw, _mainStarsRatio, _mainMeanRatio, _snrBaseline, _hfdBaseline));
        if (loss is GuideLoss.Dew or GuideLoss.GuideFocus or GuideLoss.Faint or GuideLoss.Cloud && raw.Guiding) await DegradeAsync(loss, ct);
        else if (raw.Guiding && _snrBaseline > 0 && raw.Snr.Count >= GuideWatch.Recent && raw.Snr.TakeLast(GuideWatch.Recent).Average() > _snrBaseline * 1.5)
            await StepGuideExposureAsync(-1, ct); // 다시 밝아지면 짧게 (하모닉 적도의는 짧은 노출이 좋음)
        return raw.Guiding ? GuideLoss.None : loss;
    }

    /// <summary>가이딩은 되지만 별이 약해짐: 가이드 노출 한 단계 올림 + 원인별 알림 (같은 원인은 5분에 한 번)</summary>
    private async Task DegradeAsync(GuideLoss loss, CancellationToken ct)
    {
        if (_lastDegrade.TryGetValue(loss, out var at) && DateTimeOffset.Now - at < TimeSpan.FromMinutes(5)) return;
        _lastDegrade[loss] = DateTimeOffset.Now;
        var raised = await StepGuideExposureAsync(+1, ct);
        var exp = devices.GuideExposureMs / 1000.0;
        switch (loss)
        {
            case GuideLoss.Dew:
                var heater = await devices.DewHeaterBoostAsync(ct);
                Note($"가이드 망원경에 이슬이 맺히는 것 같아요 · {(heater ? "열선을 올렸어요" : "렌즈를 확인해 주세요")}{(raised ? $" · 가이드 노출 {exp:0.#}초" : "")}", Tone.Warn, 20);
                break;
            case GuideLoss.GuideFocus:
                Note($"가이드 별이 커지고 있어요 · 가이드 망원경 초점을 확인해 주세요{(raised ? $" · 가이드 노출 {exp:0.#}초" : "")}", Tone.Warn, 20);
                break;
            case GuideLoss.Cloud:
                Note($"옅은 구름이 지나가요 · 가이드 노출을 {exp:0.#}초로 늘렸어요", Tone.Warn, 12);
                break;
            default:
                if (raised) Note($"가이드 별이 약해져 가이드 노출을 {exp:0.#}초로 늘렸어요", Tone.Warn, 12);
                break;
        }
    }

    /// <summary>가이드 노출을 한 단계 위(+1)·아래(−1)로 (PHD2 목록 값만). 바꿨으면 true</summary>
    private async Task<bool> StepGuideExposureAsync(int dir, CancellationToken ct)
    {
        var now = devices.GuideExposureMs;
        var i = Array.FindIndex(GuideExposuresMs, v => v >= now);
        if (i < 0) i = GuideExposuresMs.Length - 1;
        var j = Math.Clamp(i + dir, 0, GuideExposuresMs.Length - 1);
        if (GuideExposuresMs[j] == now) return false;
        return await devices.SetGuideExposureAsync(GuideExposuresMs[j], ct);
    }

    /// <summary>한 장 찍는 동안 가이드 별을 지켜본다. 적도의 멈춤·끊김·크게 튐이면 바로, 잃은 시간이 노출의 20%(최소 20초)를 넘으면 이 장을 멈추고 버린다</summary>
    private async Task<(FrameShot Shot, GuideLoss Lost)> ExposeWatchedAsync(PrepContext ctx, CancellationToken ct)
    {
        var guidePoll = 0;
        var expose = devices.ExposeAsync(ctx.ExposureSeconds, ctx.Plan.Iso, s =>
        {
            Set(() => _elapsed = s);
            if (mode.Simulate || ++guidePoll % 5 == 0) _ = PollGuideAsync(ct);
        }, ct);
        var lost = GuideLoss.None;
        DateTimeOffset? lostAt = null;
        var limit = TimeSpan.FromSeconds(Math.Max(20, ctx.ExposureSeconds * 0.2));
        while (!expose.IsCompleted)
        {
            await Task.WhenAny(expose, Task.Delay(PollMs, ct));
            if (expose.IsCompleted) break;
            var l = await CheckGuideAsync(ct);
            if (!l.IsLost()) { lostAt = null; continue; }
            lostAt ??= DateTimeOffset.Now;
            if (l is GuideLoss.MountStopped or GuideLoss.Disconnected or GuideLoss.Jump || DateTimeOffset.Now - lostAt > limit)
            {
                lost = l;
                await devices.AbortExposureAsync(ct);
                break;
            }
        }
        var shot = await expose;
        return (shot, lost);
    }

    /// <summary>가이드 별을 잃음: 원인별 대응. 촬영을 이어 가면 true, 끝냈으면 false</summary>
    private async Task<bool> HandleLossAsync(PrepContext ctx, GuideLoss loss, CancellationToken ct)
    {
        switch (loss)
        {
            case GuideLoss.MountStopped:
                Note("적도의가 추적을 멈췄어요 (한계·전원 확인) · 촬영을 끝냈어요", Tone.Fail, 600);
                await EndAsync("mount", ct);
                return false;

            case GuideLoss.Disconnected:
                Pause("guider", "가이드 카메라 연결을 다시 하는 중이에요");
                for (var i = 0; i < 2; i++)
                    if (await devices.ReconnectGuiderAsync(ct) && await devices.ResumeGuidingAsync(ct)) { Resume("가이드 카메라를 다시 연결했어요 · 이어서 찍어요"); return true; }
                Resume(null);
                Note("가이드 카메라(PHD2) 연결이 끊겼어요 · 연결을 확인해 주세요", Tone.Fail, 600);
                await EndAsync("guider", ct);
                return false;

            case GuideLoss.Jump:
                // 바람·케이블: 바로 별을 다시 고름. 10분에 3번이면 알림
                _jumps.Add(DateTimeOffset.Now);
                _jumps.RemoveAll(t => DateTimeOffset.Now - t > TimeSpan.FromMinutes(10));
                Pause("jump", "별이 갑자기 움직였어요 · 별을 다시 고르는 중이에요");
                var ok = await devices.ReselectStarAsync(ct) && await devices.ResumeGuidingAsync(ct);
                if (!ok && !await devices.RecenterAsync(ctx, ct)) return await WaitForStarAsync(ctx, GuideLoss.Cloud, ct);
                Resume(_jumps.Count >= 3 && !_windWarned ? "별이 자주 튀어요 · 바람이나 케이블을 확인해 주세요" : "가이드 별을 다시 잡았어요 · 이어서 찍어요");
                if (_jumps.Count >= 3) _windWarned = true;
                return true;

            case GuideLoss.Light:
                return await WaitForStarAsync(ctx, GuideLoss.Light, ct);

            default:
                return await WaitForStarAsync(ctx, GuideLoss.Cloud, ct);
        }
    }

    /// <summary>
    /// 별이 돌아오기를 기다림: 빛은 같은 자리에서 2분(별을 새로 고르지 않음, 넘으면 구름처럼), 구름은 30초마다 가이딩을 다시 켜 보며 30분.
    /// 돌아오면 10분 넘게 멈췄던 경우 다시 가운데로. 상한을 넘으면 촬영을 멈추고 알린다
    /// </summary>
    private async Task<bool> WaitForStarAsync(PrepContext ctx, GuideLoss cause, CancellationToken ct)
    {
        var key = GuideWatch.Key(cause);
        Pause(key, cause == GuideLoss.Light ? "강한 빛이 들어왔어요 · 같은 자리에서 기다리는 중" : "별이 돌아오기를 기다리는 중");
        var since = DateTimeOffset.Now;
        var lastTry = DateTimeOffset.MinValue;
        // PHD2는 별이 같은 자리에 돌아오면 스스로 다시 잡는다(LostLock → Guiding, 2026-10-07 시뮬레이터 실기) →
        // 처음 3분은 손대지 않고, 그 뒤 2분마다 별을 다시 골라 본다 (오래 멈춰 별이 흘렀을 수 있음)
        var hands = mode.Simulate ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromMinutes(3);
        var retry = mode.Simulate ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromMinutes(2);
        while (true)
        {
            if (_stopRequested) { Resume(null); return true; }
            var raw = await devices.GuideRawAsync(ct);
            if (!raw.MountTracking) { Resume(null); return await HandleLossAsync(ctx, GuideLoss.MountStopped, ct); }
            if (raw.Guiding) break;
            var waited = DateTimeOffset.Now - since;
            if (cause == GuideLoss.Light && waited > LightWait) { cause = GuideLoss.Cloud; key = "cloud"; Pause(key, "별이 돌아오기를 기다리는 중", keepSince: true); }
            if (cause == GuideLoss.Cloud && waited > CloudWait)
            {
                Resume(null);
                Note("별이 30분 넘게 돌아오지 않아 촬영을 멈췄어요 · 하늘을 확인해 주세요", Tone.Fail, 600);
                await EndAsync("cloud", ct);
                return false;
            }
            if (cause == GuideLoss.Cloud && waited > hands && DateTimeOffset.Now - lastTry > retry) { lastTry = DateTimeOffset.Now; await devices.ReselectStarAsync(ct); }
            Set(() => { _note = new StatusLine($"멈춤 · {(cause == GuideLoss.Light ? "강한 빛이 지나가기를" : "별이 돌아오기를")} 기다리는 중 · {(int)waited.TotalMinutes}분째", Tone.Warn); _noteUntil = DateTimeOffset.MaxValue; });
            await Task.Delay(PollMs, ct);
        }
        if (DateTimeOffset.Now - since > RecenterAfter) await devices.RecenterAsync(ctx, ct);
        Resume("별이 돌아왔어요 · 이어서 찍어요");
        return true;
    }

    private void Pause(string key, string text, bool keepSince = false) => Set(() =>
    {
        if (!keepSince || _pause is null) _pausedSince = DateTimeOffset.Now;
        _pause = key;
        _mode = ShootMode.Paused;
        _note = new StatusLine(text, Tone.Warn);
        _noteUntil = DateTimeOffset.MaxValue;
    });

    private void Resume(string? message)
    {
        Set(() =>
        {
            if (_pause is { } k) _pausedMinutes[k] = _pausedMinutes.GetValueOrDefault(k) + (DateTimeOffset.Now - _pausedSince).TotalMinutes;
            _pause = null;
            _mode = ShootMode.Shoot;
            _note = null;
        });
        if (message is not null) Note(message, Tone.Ok);
    }

    private async Task PollGuideAsync(CancellationToken ct)
    {
        try
        {
            if (await devices.GuideRmsAsync(ct) is { } g)
                Set(() => { _guide.Add(g); if (_guide.Count > Spark) _guide.RemoveAt(0); });
        }
        catch (Exception) { /* 가이딩 값은 다음에 */ }
    }

    private static int Median(List<int> xs) => xs.Order().ElementAt(xs.Count / 2);

    /// <summary>끝: 가이딩을 멈추고(마무리·다른 대상 모두 필요) 끝 이유를 남긴다</summary>
    private async Task EndAsync(string reason, CancellationToken ct)
    {
        Set(() => { _mode = ShootMode.Finishing; _note = new StatusLine("가이딩을 멈추고 확인하는 중입니다", Tone.Busy); _noteUntil = DateTimeOffset.MaxValue; });
        var stopped = false;
        try { stopped = await devices.StopGuidingAsync(ct); } catch (Exception e) { log.LogWarning(e, "가이딩 정지 실패"); }
        Set(() =>
        {
            _mode = ShootMode.Ended;
            _ended = reason;
            _note = stopped ? new StatusLine("가이딩을 멈췄어요", Tone.Ok) : new StatusLine("가이딩이 멈췄는지 확인하지 못했어요. PHD2를 확인해 주세요", Tone.Warn);
            _noteUntil = DateTimeOffset.MaxValue;
        });
        if (_ctx is { } c) Record(c);
        log.LogInformation("촬영 끝: {Reason} · 쓸 사진 {Good} · 제외 {Excluded}", reason, _good, _excluded);
    }

    /// <summary>그날 밤 결과 기록에 이 촬영(대상 하나)을 남긴다</summary>
    private void Record(PrepContext ctx)
    {
        lock (_gate)
        {
            var mine = new TargetShots(ctx.Plan.TargetId, ctx.TargetName, _good, _excluded, ctx.ExposureSeconds, ctx.Plan.Iso, new Dictionary<string, int>(_tally), _folder,
                new Dictionary<string, double>(_pausedMinutes));
            var list = ctx.Results.Get<NightShootResult>()?.Targets.ToList() ?? [];
            // 이 촬영(계획 하나)의 줄: 처음이면 더하고, 이후엔 바꾼다
            if (_recordIndex >= 0 && _recordIndex < list.Count) list[_recordIndex] = mine;
            else { list.Add(mine); _recordIndex = list.Count - 1; }
            ctx.Results.Set(new NightShootResult(list));
        }
    }

    private int _recordIndex = -1;

    /// <summary>[테스트] 촬영이 끝날 때까지 기다린다</summary>
    internal Task Running => _run;
}
