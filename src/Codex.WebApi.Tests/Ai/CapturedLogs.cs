using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Ai;

// An ILoggerProvider that keeps every formatted message and exception, so a test can prove a secret never reached a log.
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public string Text => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new Capture(_lines);

    public void Dispose()
    {
    }

    private sealed class Capture(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue(formatter(state, exception) + exception);
    }
}
