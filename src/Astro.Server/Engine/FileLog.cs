using System.Collections.Concurrent;
using Astro.Core;

namespace Astro.Server.Engine;

/// <summary>
/// 아이라 기록 파일 (2026-10-10 사용자 요청 — 완성 전까지 모든 단계에 로그를 남겨 문제를 찾기 쉽게).
/// 데이터 폴더 logs\aira-날짜.log에 한 줄씩: 시각 · 수준 · 어디서 · 내용. 아이라 코드(Astro.*)는 Information부터, 나머지(ASP.NET 등)는 Warning부터.
/// 화면의 단계 이동·버튼은 /api/log로 들어와 같은 파일에 "화면"으로 남는다. 14일 지난 파일은 지운다
/// </summary>
public sealed class FileLogProvider : ILoggerProvider
{
    private static readonly Lock Gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.DataFolder, "logs");

    public FileLogProvider()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var f in new DirectoryInfo(Dir).EnumerateFiles("aira-*.log"))
                if (f.LastWriteTime < DateTime.Now.AddDays(-14)) f.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public ILogger CreateLogger(string category) => _loggers.GetOrAdd(category, c => new FileLogger(c));
    public void Dispose() { }

    /// <summary>한 줄 쓰기 (다른 곳에서도 — 화면 기록 등)</summary>
    public static void Write(string level, string source, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {source}: {message.Replace("\r", "").Replace("\n", " ⏎ ")}";
        try
        {
            lock (Gate) File.AppendAllText(Path.Combine(Dir, $"aira-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed class FileLogger(string category) : ILogger
    {
        private readonly bool _ours = category.StartsWith("Astro.", StringComparison.Ordinal);
        private readonly string _short = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= (_ours ? LogLevel.Information : LogLevel.Warning);

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (!IsEnabled(level)) return;
            var text = format(state, error);
            if (error is not null) text += $" — {error.GetType().Name}: {error.Message}";
            Write(level switch { LogLevel.Warning => "WARN", LogLevel.Error => "ERROR", LogLevel.Critical => "CRIT", LogLevel.Debug => "DEBUG", _ => "INFO" }, _short, text);
        }
    }
}
