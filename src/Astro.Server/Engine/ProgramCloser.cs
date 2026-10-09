using System.Diagnostics;

namespace Astro.Server.Engine;

/// <summary>
/// N.I.N.A.·PHD2 닫기 — 마무리의 장비 정리와 아이라 종료가 함께 쓴다.
/// 아이라 종료 때는 아이라가 켠 N.I.N.A.만 닫는다(<see cref="LaunchedNina"/>): 사용자가 먼저 켜 둔 N.I.N.A.는 그대로 둔다.
/// 아이라가 꺼진 뒤 N.I.N.A.만 남으면 아이라가 숨기던 N.I.N.A. 알림이 그대로 뜬다 (2026-10-09 — 종료 순간의 PHD2 오류 두 개)
/// </summary>
public sealed class ProgramCloser(NinaWatcher watcher, ILogger<ProgramCloser> log)
{
    private volatile bool _launched;

    /// <summary>이번에 아이라가 N.I.N.A.를 켰는가 (엔진 켜기). PHD2는 N.I.N.A.가 가이더를 연결하며 켠다</summary>
    public bool LaunchedNina => _launched;
    public void NinaLaunched() => _launched = true;

    /// <summary>PHD2 → N.I.N.A. 순으로 창을 닫게 하고 15초씩 기다린다. 다 닫혔으면 true (저장 확인 창 등으로 안 닫히면 false)</summary>
    public async Task<bool> CloseAsync(CancellationToken ct)
    {
        var all = true;
        watcher.ExpectExit(); // 정상 종료 — 화면에 "N.I.N.A.가 꺼졌어요"를 띄우지 않는다 (CX-APP-R4)
        foreach (var name in new[] { "phd2", "NINA" })
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
        if (all) _launched = false;
        return all;
    }
}
