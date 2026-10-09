using System.Text.Json;
using Astro.Server.Prepare.Tasks.Focus;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// ④ 초점 실장비: 포커서 이동·위치·기온은 N.I.N.A. 포커서, 자동초점은 N.I.N.A. 자동초점(auto-focus → last-af 기록, 이벤트 AUTOFOCUS-FINISHED/ERROR-AF).
/// 2026-10-05 실기: Oasis 이동 정상, 범위 0~56000(제조사 설정 — 사용자가 0·최대를 설정 창에서 정함), 프로브 기온은 연결할 때만 잡힘.
/// 자동초점은 별이 필요해 맑은 날 확인. 지점별 곡선은 N.I.N.A.가 끝난 뒤 기록으로만 줘서, 끝난 다음 한꺼번에 그린다.
/// </summary>
public sealed class RealFocusDevices(NinaRig rig, IConfiguration config, FocuserZeroRequest zero, Engine.OpticsStore optics) : IFocusDevices
{
    /// <summary>N.I.N.A. 자동초점이 시작점에서 한쪽으로 가는 거리 = 처음 칸 수 × 칸 크기 (+ 백래시 보정 여유)</summary>
    public async Task<int> AutofocusReachAsync(CancellationToken ct)
    {
        if (await rig.ProfileAsync(ct) is not { ValueKind: JsonValueKind.Object } p || !p.TryGetProperty("FocuserSettings", out var f)) return 0;
        var steps = NinaRig.Num(f, "AutoFocusInitialOffsetSteps");
        var size = NinaRig.Num(f, "AutoFocusStepSize");
        var backlash = Math.Max(NinaRig.Num(f, "BacklashIn"), NinaRig.Num(f, "BacklashOut"));
        if (!double.IsFinite(steps) || !double.IsFinite(size) || steps <= 0 || size <= 0) return 0;
        return (int)Math.Ceiling(steps * size + (double.IsFinite(backlash) ? backlash : 0) + size);
    }

    private static string MemoryFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Astro.Core.Product.DataFolder, "focus-memory.json");
    private sealed record Saved(int Position, double? TemperatureC, DateTimeOffset At);

    /// <summary>망원경별로 지난번 맞은 초점 위치 (focus-memory.json)</summary>
    public (int Position, double? TemperatureC)? SavedFocus
    {
        get
        {
            try
            {
                if (optics.Current is not { } scope || !File.Exists(MemoryFile)) return null;
                var all = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(MemoryFile));
                return all is not null && all.TryGetValue(scope.Id, out var s) ? (s.Position, s.TemperatureC) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
    }

    public void SaveFocus(int position, double? temperatureC)
    {
        if (optics.Current is not { } scope) return;
        try
        {
            var all = File.Exists(MemoryFile) ? JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(MemoryFile)) ?? [] : [];
            all[scope.Id] = new Saved(position, temperatureC, DateTimeOffset.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(MemoryFile)!);
            File.WriteAllText(MemoryFile, JsonSerializer.Serialize(all));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public bool ZeroRequested => zero.Pending;
    public void DropZeroRequest() => zero.Pending = false;

    /// <summary>
    /// Oasis면: N.I.N.A. 포커서 연결 끊기 → 제조사 SDK로 0점 → 다시 연결 → N.I.N.A.가 읽는 위치가 0인지 확인.
    /// Oasis가 아니면 Unsupported (ASCOM·N.I.N.A.에는 0점 명령이 없음)
    /// </summary>
    public async Task<FocuserZero> ZeroIfRequestedAsync(CancellationToken ct)
    {
        if (!zero.Pending) return FocuserZero.None;
        var name = await rig.FocuserNameAsync(ct) ?? "";
        if (!name.Contains("Oasis", StringComparison.OrdinalIgnoreCase) && !name.Contains("AOFocuser", StringComparison.OrdinalIgnoreCase) || !OasisSdk.Installed)
            return new FocuserZero(true, false, Unsupported: true);
        if ((await rig.FocuserAsync(ct))?.Connected == true && !await rig.DisconnectAsync("focuser", ct))
            return new FocuserZero(true, false, Problem: "N.I.N.A.에서 포커서 연결을 끊지 못했습니다");
        // 포커서가 여럿이면 드라이버에 저장된 일련번호로 고른다 (Codex E03)
        var serial = OperatingSystem.IsWindows() ? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\ASCOM\Focuser Drivers\ASCOM.AOFocuser.Focuser")?.GetValue("SerialNumber") as string : null;
        var problem = await Task.Run(() => OasisSdk.SetZero(serial), ct);
        // 0점이 안 됐어도 포커서는 다시 연결해 둔다
        var back = await rig.ConnectAsync("focuser", ct) && (await rig.FocuserAsync(ct))?.Connected == true;
        if (!back) problem = (problem is null ? "" : problem + " · ") + "N.I.N.A.에 포커서를 다시 연결하지 못했습니다";
        else if (problem is null && (await rig.FocuserAsync(ct))?.Position is { } p && p != 0) problem = $"0점을 잡았지만 N.I.N.A.가 읽는 위치가 {p}입니다";
        if (problem is not null) return new FocuserZero(true, false, Problem: problem);
        zero.Pending = false;
        return new FocuserZero(true, true);
    }

    /// <summary>포커서 최대 위치 (N.I.N.A.가 알려 주지 않아 설정 Prepare:FocuserMax, 기본은 Oasis 기본값)</summary>
    private int Max => config.GetValue("Prepare:FocuserMax", 56000);

    public Task<(int Min, int Max)> LimitsAsync(CancellationToken ct) => Task.FromResult((0, Max));

    public async Task<int> PositionAsync(CancellationToken ct) => (await rig.FocuserAsync(ct))?.Position ?? 0;

    public async Task<double?> TemperatureAsync(CancellationToken ct) => (await rig.FocuserAsync(ct))?.Temperature;

    public async Task<FocuserMove> MoveAsync(int position, CancellationToken ct)
    {
        var problem = await rig.MoveFocuserAsync(Math.Clamp(position, 0, Max), ct);
        return problem is null ? new FocuserMove(true) : new FocuserMove(false, Stalled: problem.Contains("멈췄"), problem);
    }

    /// <summary>자동초점 측정점 사이 최대 간격 (노출 + 포커서 이동은 보통 수십 초)</summary>
    public static readonly TimeSpan Stall = TimeSpan.FromMinutes(2); // 2026-10-08 실기: 포커서 오류 뒤 3분은 길게 느껴짐

    public async Task<AutofocusRun> AutofocusAsync(Action<int, double> point, CancellationToken ct)
    {
        var since = DateTimeOffset.Now;
        if (!await rig.StartAutofocusAsync(ct)) return new AutofocusRun(false, true, 0, 0, false, Problem: "자동초점을 시작하지 못했습니다");
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(15);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000, ct);
            // N.I.N.A. 자동초점이 끝·실패 신호 없이 멈추는 일이 있다(2026-10-08 시뮬레이터: 측정점 5개 뒤 조용히 멈춤, 취소해야 끝남) →
            // 측정점(AUTOFOCUS-POINT-ADDED)이 Stall 동안 늘지 않으면 멈춘 것으로 보고 취소
            var progress = await rig.LastEventAtAsync("AUTOFOCUS", since, ct) ?? since;
            if (DateTimeOffset.Now - progress > Stall)
            {
                var stopped = await CancelAndConfirmAsync(ct);
                return new AutofocusRun(false, true, 0, 0, false,
                    Problem: stopped ? $"자동초점이 {Stall.TotalMinutes:0}분 넘게 진행되지 않아 멈췄습니다" : "자동초점이 진행되지 않아 취소했지만 멈췄는지 확인하지 못했습니다",
                    StopUnconfirmed: !stopped);
            }
            var events = await rig.EventsSinceAsync(since, ct);
            if (events.Contains("ERROR-AF"))
                // N.I.N.A. 자동초점 실패는 대부분 별을 못 찾은 경우 (초점이 크게 나갔거나 구름·덮개)
                return new AutofocusRun(false, false, 0, 0, false, Problem: "자동초점에서 별을 찾지 못했습니다");
            if (!events.Contains("AUTOFOCUS-FINISHED")) continue;
            if (await rig.LastAutofocusAsync(ct) is not { } af) break;
            var points = new List<(int, double)>();
            if (af.TryGetProperty("MeasurePoints", out var mp) && mp.ValueKind == JsonValueKind.Array)
                foreach (var p in mp.EnumerateArray())
                    if (NinaRig.Num(p, "Value") is var v && double.IsFinite(v)) points.Add(((int)NinaRig.Num(p, "Position"), v));
            foreach (var (pos, hfr) in points) point(pos, hfr);
            var best = af.GetProperty("CalculatedFocusPoint");
            var r2 = af.TryGetProperty("RSquares", out var rs) ? Best(rs) : double.NaN;
            return new AutofocusRun(true, points.Count > 0, (int)NinaRig.Num(best, "Position"), NinaRig.Num(best, "Value"),
                CurveGood: !double.IsFinite(r2) || r2 >= 0.7, Problem: double.IsFinite(r2) && r2 < 0.7 ? $"곡선이 고르지 않습니다 (R² {r2:F2})" : null);
        }
        var done = await CancelAndConfirmAsync(ct);
        return new AutofocusRun(false, true, 0, 0, false, Problem: done ? "자동초점이 끝나지 않았습니다" : "자동초점이 끝나지 않아 취소했지만 멈췄는지 확인하지 못했습니다", StopUnconfirmed: !done);
    }

    /// <summary>
    /// 자동초점 취소 + 멈춤 확인 (CX-NIGHT-06): 취소 응답만 믿지 않고, 카메라가 노출 중이 아니고 포커서가 멈춘 상태가 두 번 이어지면 멈춘 것으로 본다(최대 30초)
    /// </summary>
    private async Task<bool> CancelAndConfirmAsync(CancellationToken ct)
    {
        await rig.CancelAutofocusAsync(ct);
        var quiet = 0;
        for (var i = 0; i < 15 && quiet < 2; i++)
        {
            await Task.Delay(2000, ct);
            var idle = !await rig.CameraExposingAsync(ct) && await rig.FocuserAsync(ct) is { Moving: false };
            quiet = idle ? quiet + 1 : 0;
        }
        return quiet >= 2;
    }

    /// <summary>범위 안에서 넓은 간격으로 사진을 찍어 별이 보이는 위치를 찾는다 (2초 노출, 별 5개 이상)</summary>
    public async Task<int?> CoarseSearchAsync(int min, int max, int from, Action<int> visiting, CancellationToken ct)
    {
        var step = Math.Max(1, (max - min) / 8);
        var spots = Enumerable.Range(0, 9).Select(i => min + i * step).OrderBy(p => Math.Abs(p - from)).ToList();
        foreach (var p in spots)
        {
            visiting(p);
            if ((await MoveAsync(p, ct)).Ok is false) return null;
            var shot = await rig.CaptureAsync(2, solve: false, save: false, null, ct);
            if (shot.Ok && await rig.LastStatsAsync(ct) is { Stars: >= 5 }) return p;
        }
        return null;
    }

    public async Task<bool> StopAsync(CancellationToken ct)
    {
        await rig.CancelAutofocusAsync(ct);
        await rig.StopFocuserAsync(ct);
        for (var i = 0; i < 6; i++)
        {
            if (await rig.FocuserAsync(ct) is { Moving: false }) return true;
            await Task.Delay(500, ct);
        }
        return false;
    }

    public async Task<FocusEndState> ReadEndStateAsync(CancellationToken ct) =>
        await rig.FocuserAsync(ct) is { } f ? new FocusEndState(f.Moving, f.Position, false) : new FocusEndState(false, -1, true);

    private static double Best(JsonElement rs)
    {
        var best = double.NaN;
        if (rs.ValueKind != JsonValueKind.Object) return best;
        foreach (var p in rs.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && (double.IsNaN(best) || p.Value.GetDouble() > best)) best = p.Value.GetDouble();
        return best;
    }
}
