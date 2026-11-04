using Microsoft.Extensions.Logging;

namespace Codex.UnitTest.Contracts;

// Keeps what was logged so a test can read it.
public sealed class CapturingLogger<T> : ILogger<T>
{
    public IList<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}
