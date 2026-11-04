using System.Collections.Concurrent;
using Agoda.IoC.Core;

namespace Codex.Application.Identity;

// The per-email half of sign-in rate limiting (the per-IP half is the web host's rate limiter): at most MaxAttempts
// attempts per email in any Window, whether or not the email belongs to anyone. In memory, per process.
[RegisterSingleton]
public sealed class SignInAttempts(TimeProvider clock)
{
    public const int MaxAttempts = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.OrdinalIgnoreCase);

    // Records an attempt; false when this email has used up its attempts for now.
    public bool TryRecord(string email)
    {
        var now = clock.GetUtcNow();
        var attempts = _attempts.GetOrAdd(email.Trim(), _ => new Queue<DateTimeOffset>());
        lock (attempts)
        {
            while (attempts.TryPeek(out var oldest) && now - oldest >= Window)
            {
                attempts.Dequeue();
            }
            if (attempts.Count >= MaxAttempts)
            {
                return false;
            }
            attempts.Enqueue(now);
            return true;
        }
    }
}
