using System.Diagnostics;
using Astro.Core;

namespace Astro.Server.Engine;

/// <summary>
/// 장비 연결 시간 기록 (2026-10-10 사용자 요청 — 장비 연결이 오래 걸리는 이유를 로그로 보고 다시 이야기하기로).
/// 데이터 폴더의 logs\connect-날짜.log에 장비마다 한 줄: 시각 · 장비 · 결과 · 총 시간 · 단계별 시간(점검·목록 기다림·연결 등)
/// </summary>
public sealed class ConnectTimingLog
{
    private static readonly Lock Gate = new();
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Stopwatch _step = Stopwatch.StartNew();
    private readonly List<string> _steps = [];

    /// <summary>방금 끝난 단계와 걸린 시간을 적어 둔다 (다음 단계의 시간은 여기서부터)</summary>
    public void Step(string name)
    {
        _steps.Add($"{name} {_step.ElapsedMilliseconds}ms");
        _step.Restart();
    }

    /// <summary>장비 하나를 끝내며 한 줄로 남긴다</summary>
    public void Write(string kind, string status, string? note = null)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {kind,-11} {status,-8} {_total.ElapsedMilliseconds,6}ms  {string.Join(" · ", _steps)}{(note is null ? "" : $"  — {note}")}";
        Append(line);
    }

    public static void Append(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "logs");
            Directory.CreateDirectory(dir);
            lock (Gate) File.AppendAllText(Path.Combine(dir, $"connect-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
