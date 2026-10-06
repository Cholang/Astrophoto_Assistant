using System.Diagnostics;
using System.Runtime.CompilerServices;
using Astro.Nina;

namespace Astro.Server.Engine;

public enum NinaState
{
    /// <summary>아직 지켜보지 않음 (1단계 엔진 켜기 전)</summary>
    Unknown,
    Running,
    /// <summary>N.I.N.A. 프로세스가 끝났다 (사용자가 껐거나 멈춰서 꺼짐)</summary>
    Exited,
    /// <summary>켜져 있지만 응답하지 않는다 (멈춤)</summary>
    NotResponding,
    /// <summary>AA가 마무리에서 일부러 닫음 (경고 아님)</summary>
    Closed,
}

/// <summary>
/// N.I.N.A. 감시. 1단계에서 N.I.N.A.와 연결되면 지켜보기 시작한다.
/// - 프로세스가 끝나는 순간은 운영체제가 알려 준다 (Process.Exited — 폴링 없이 즉시)
/// - 켜져 있는데 멈춘 경우는 10초마다 짧게 물어봐서 잡는다 (3번 연속 무응답 = 약 30초)
/// 로컬 요청이라 부담은 거의 없다. 상태가 바뀌면 화면(/api/nina/watch)에 알린다.
/// 나중에: Advanced API의 실시간 연결(WebSocket)로 장비 끊김·촬영 진행도 받는다 (촬영 화면 만들 때)
/// </summary>
public sealed class NinaWatcher(NinaApiClient nina, ILogger<NinaWatcher> log) : IAsyncDisposable
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(10);
    private const int MissesForHang = 3;

    private readonly Lock _gate = new();
    private readonly List<TaskCompletionSource> _waiters = [];
    private Process? _process;
    private CancellationTokenSource? _poll;
    private NinaState _state = NinaState.Unknown;
    private int _version; // 상태가 바뀔 때마다 1씩 (지켜보는 쪽이 놓치지 않게)

    public NinaState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>1단계에서 N.I.N.A.와 연결되면 부른다. 다시 켠 뒤에도 부른다.</summary>
    public void Track()
    {
        Stop();
        Volatile.Write(ref _expectExit, false);
        var p = Process.GetProcessesByName("NINA").FirstOrDefault();
        if (p is null)
        {
            Set(NinaState.Exited);
            return;
        }
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                if (Volatile.Read(ref _expectExit)) { log.LogInformation("N.I.N.A.를 닫았습니다 (마무리)"); Set(NinaState.Closed); return; }
                log.LogWarning("N.I.N.A.가 종료되었습니다");
                Set(NinaState.Exited);
            };
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.LogWarning(e, "N.I.N.A. 프로세스 종료 알림을 걸지 못했습니다. 응답 확인만으로 지켜봅니다");
        }
        lock (_gate) _process = p;
        Set(NinaState.Running);

        var cts = new CancellationTokenSource();
        lock (_gate) _poll = cts;
        _ = PollAsync(cts.Token);
    }

    private bool _expectExit;

    /// <summary>마무리에서 AA가 N.I.N.A.를 닫기 직전: 이번 종료는 정상 (CX-APP-R4)</summary>
    public void ExpectExit() => Volatile.Write(ref _expectExit, true);

    /// <summary>[임시] 화면 설계용: N.I.N.A.가 꺼진 것처럼 (Equipment:Simulate일 때만 엔드포인트가 부른다)</summary>
    public void SimulateExit()
    {
        Stop();
        Set(NinaState.Exited);
    }

    /// <summary>지금 상태를 먼저 보내고, 바뀔 때마다 보낸다 (화면이 떠 있는 동안)</summary>
    public async IAsyncEnumerable<NinaState> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        int seen;
        lock (_gate) seen = _version;
        yield return State;
        while (!ct.IsCancellationRequested)
        {
            Task changed;
            TaskCompletionSource? mine = null;
            lock (_gate)
            {
                if (_version != seen) changed = Task.CompletedTask; // 보내는 사이에 바뀌었다
                else
                {
                    mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Add(mine);
                    changed = mine.Task;
                }
            }
            var cancelled = false;
            try
            {
                // SSE 연결이 오래 조용하면 중간에서 끊길 수 있어 30초마다 한 번은 지금 상태를 다시 보낸다
                await changed.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { cancelled = true; }
            finally
            {
                // 깨지 않은 채 끝난 대기자는 목록에서 치운다 (같은 상태가 오래가도 쌓이지 않게 — Codex 16절 최적화 2)
                if (mine is not null) lock (_gate) _waiters.Remove(mine);
            }
            if (cancelled) yield break;
            lock (_gate) seen = _version;
            yield return State;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var misses = 0;
        using var timer = new PeriodicTimer(PollEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var ok = await nina.GetVersionAsync(ct) is not null;
                if (ok)
                {
                    misses = 0;
                    if (State == NinaState.NotResponding) Set(NinaState.Running);
                    continue;
                }
                bool alive;
                lock (_gate) alive = _process is { HasExited: false };
                if (!alive)
                {
                    Set(Volatile.Read(ref _expectExit) ? NinaState.Closed : NinaState.Exited);
                    return;
                }
                if (++misses >= MissesForHang)
                {
                    log.LogWarning("N.I.N.A.가 {Seconds}초 동안 응답하지 않습니다", misses * PollEvery.TotalSeconds);
                    Set(NinaState.NotResponding);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Set(NinaState state)
    {
        List<TaskCompletionSource> wake;
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
            _version++;
            wake = [.. _waiters];
            _waiters.Clear();
        }
        foreach (var w in wake) w.TrySetResult();
    }

    private void Stop()
    {
        lock (_gate)
        {
            _poll?.Cancel();
            _poll?.Dispose();
            _poll = null;
            _process?.Dispose();
            _process = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
