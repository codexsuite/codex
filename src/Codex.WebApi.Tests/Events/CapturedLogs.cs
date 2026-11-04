using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Events;

// Every line one host logs, at every level: ReplaceLoggingIn swaps the host's logger factory (Serilog's, which ignores
// other ILoggerProviders) for one that writes here only. WaitForAsync completes when the nth line matching a condition
// arrives, so a test waits for the log rather than for time.
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<CapturedLog> _entries = [];
    private readonly List<(Func<CapturedLog, bool> Match, int Count, TaskCompletionSource<CapturedLog> Done)> _waiters = [];

    public void ReplaceLoggingIn(IServiceCollection services)
        => services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(this)));

    public IReadOnlyList<CapturedLog> Matching(Func<CapturedLog, bool> match)
    {
        lock (_entries)
        {
            return _entries.Where(match).ToList();
        }
    }

    public async Task<CapturedLog> WaitForAsync(Func<CapturedLog, bool> match, int count = 1)
    {
        var done = new TaskCompletionSource<CapturedLog>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_entries)
        {
            var seen = _entries.Where(match).ToList();
            if (seen.Count >= count)
            {
                return seen[count - 1];
            }
            _waiters.Add((match, count - seen.Count, done));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var giveUp = timeout.Token.Register(() => done.TrySetCanceled(timeout.Token));
        return await done.Task;
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
        // Nothing to release: the entries stay readable after the host has gone.
    }

    private void Add(CapturedLog entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var (match, count, done) = _waiters[i];
                if (!match(entry))
                {
                    continue;
                }
                if (count == 1)
                {
                    _waiters.RemoveAt(i);
                    done.TrySetResult(entry);
                }
                else
                {
                    _waiters[i] = (match, count - 1, done);
                }
            }
        }
    }

    private sealed class Logger(CapturedLogs logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => logs.Add(new CapturedLog(category, logLevel, formatter(state, exception)));
    }
}
