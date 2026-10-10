using System.Diagnostics;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A.·PHD2·SharpCap·Wanderer Empire 닫기 — 마무리의 장비 정리와 아이라 종료가 함께 쓴다.
/// 아이라 종료 때는 아이라가 켠 N.I.N.A.만 닫는다(<see cref="LaunchedNina"/>): 사용자가 먼저 켜 둔 N.I.N.A.는 그대로 둔다.
/// 아이라가 꺼진 뒤 N.I.N.A.만 남으면 아이라가 숨기던 N.I.N.A. 알림이 그대로 뜬다 (2026-10-09 — 종료 순간의 PHD2 오류 두 개).
/// 종료 창은 닫을 프로그램 목록(<see cref="Targets"/>)을 받아 하나씩 닫으며(<see cref="CloseOneAsync"/>) 체크한다 (2026-10-10 사용자 요청)
/// </summary>
public sealed class ProgramCloser(NinaWatcher watcher, ILogger<ProgramCloser> log)
{
    private volatile bool _launched;

    /// <summary>이번에 아이라가 N.I.N.A.를 켰는가 (엔진 켜기). PHD2는 N.I.N.A.가 가이더를 연결하며 켠다</summary>
    public bool LaunchedNina => _launched;
    public void NinaLaunched() => _launched = true;

    /// <summary>닫을 수 있는 프로그램: 화면에 보이는 이름</summary>
    public sealed record Target(string Id, string Name);

    /// <summary>
    /// 닫는 차례: N.I.N.A.를 먼저 (2026-10-10 기록: PHD2를 먼저 닫자 N.I.N.A.가 "PHD2 서버 연결이 끊어졌습니다" 오류 창을 띄웠고,
    /// 아이라가 꺼지는 중이라 사용자가 내용을 볼 수도 없었다) → PHD2 → SharpCap(극축 정렬 중에 끄면 남아 있을 수 있음) →
    /// Wanderer Empire(아이라가 켜진 뒤 장비 연결이 켠 것만, 그 ASCOM 서버까지 — N.I.N.A.가 닫힌 뒤에만)
    /// </summary>
    private static readonly (string Id, string Name, string[] Processes)[] Order =
    [
        ("nina", "N.I.N.A.", ["NINA"]),
        ("phd2", "PHD2", ["phd2"]),
        ("sharpcap", "SharpCap", ["SharpCap"]),
        ("empire", "Wanderer Empire", [BackgroundWindows.WandererEmpire, "ASCOM.WandererBox1", "ASCOM.WandererBox2"]),
    ];

    /// <summary>아이라(서버)가 켜진 시각 — 이 뒤에 켜진 Wanderer Empire는 아이라의 장비 연결이 켠 것</summary>
    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime;

    /// <summary>지금 떠 있어서 닫을 프로그램 (차례대로). Empire는 아이라가 켜진 뒤에 켜진 것만 — 사용자가 켜 둔 것은 그대로</summary>
    public IReadOnlyList<Target> Targets() =>
        Order.Where(o => Running(o.Id, o.Processes)).Select(o => new Target(o.Id, o.Name)).ToList();

    /// <summary>
    /// 프로그램 하나를 닫는다 (창을 닫게 하고 15초 기다림). 다 닫혔으면 true (저장 확인 창 등으로 안 닫히면 false).
    /// Empire는 N.I.N.A.가 남아 있으면 닫지 않는다 (N.I.N.A.가 파워박스를 잃으면 오류 창)
    /// </summary>
    public async Task<bool> CloseOneAsync(string id, CancellationToken ct)
    {
        var entry = Order.FirstOrDefault(o => o.Id == id);
        if (entry.Id is null) return false;
        if (id == "nina") watcher.ExpectExit(); // 정상 종료 — 화면에 "N.I.N.A.가 꺼졌어요"를 띄우지 않는다 (CX-APP-R4)
        if (id == "empire")
        {
            if (Running("nina", ["NINA"])) return false;
            CloseEmpireStartedSince(StartedAt, entry.Processes);
            return !Running(id, entry.Processes);
        }
        var all = true;
        foreach (var name in entry.Processes)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.CloseMainWindow();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { all = false; log.LogWarning("{Name}이 닫히지 않음 (저장 확인 창 등)", name); }
                }
                catch (InvalidOperationException) { /* 이미 닫힘 */ }
                finally { p.Dispose(); }
            }
        if (all) log.LogInformation("{Name}을 닫음", entry.Name);
        return all;
    }

    /// <summary>
    /// N.I.N.A. → PHD2 → SharpCap을 닫고, 다 닫혔으면 아이라가 켠 Empire까지. 다 닫혔으면 true (마무리의 장비 정리)
    /// </summary>
    public async Task<bool> CloseAsync(CancellationToken ct)
    {
        var all = true;
        watcher.ExpectExit();
        foreach (var id in new[] { "nina", "phd2", "sharpcap" })
            all &= await CloseOneAsync(id, ct);
        if (all) await CloseOneAsync("empire", ct);
        if (all) _launched = false;
        return all;
    }

    private static bool Running(string id, string[] processes)
    {
        foreach (var name in processes)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        if (id == "empire" && p.StartTime < StartedAt) continue;
                        return true;
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
        return false;
    }

    /// <summary>
    /// 아이라가 켜진 뒤에 켜진 Wanderer Empire(와 그 ASCOM 서버)를 닫는다 (2026-10-10 사용자 요청 — 아이라가 켰으면 끝낼 때도).
    /// 그 전부터 켜져 있던 Empire(사용자가 켜 둔 것)는 그대로. 파워박스 출력의 켜짐·꺼짐은 장치에 남는다
    /// </summary>
    private void CloseEmpireStartedSince(DateTime since, string[] processes)
    {
        foreach (var name in processes)
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                {
                    try
                    {
                        if (p.StartTime < since) continue;
                        if (!p.CloseMainWindow() || !p.WaitForExit(5000)) p.Kill();
                        p.WaitForExit(5000);
                        log.LogInformation("{Name}을 닫음 (아이라가 켠 것)", name);
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
    }
}
