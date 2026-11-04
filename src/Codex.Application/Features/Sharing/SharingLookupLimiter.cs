using System.Threading.RateLimiting;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Sharing;

// The sharing lookup's rate limit (#80): per user, 20 a minute and 200 a day, so an exact-email lookup cannot become a
// way to enumerate accounts. In memory per process, like the per-email half of sign-in limiting (SignInAttempts):
// only the web host serves the lookup. A call needs room in both windows.
[RegisterSingleton]
public sealed class SharingLookupLimiter : IDisposable
{
    public const int PerMinute = 20;
    public const int PerDay = 200;

    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.CreateChained(
        Window(PerMinute, TimeSpan.FromMinutes(1)),
        Window(PerDay, TimeSpan.FromDays(1)));

    // Throws rate_limited (429) once userId is over either limit.
    public void Acquire(Guid userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        if (!lease.IsAcquired)
        {
            throw ApiException.RateLimited("Too many lookups. Wait a minute and try again.");
        }
    }

    public void Dispose() => _limiter.Dispose();

    private static PartitionedRateLimiter<Guid> Window(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<Guid, Guid>(user => RateLimitPartition.GetFixedWindowLimiter(
            user, _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
}
