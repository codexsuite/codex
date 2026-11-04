using System.Threading.RateLimiting;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;

namespace Codex.WebApi.Features.Auth;

// The per-IP half of sign-in limiting (the per-email half is in SignInService): 30 attempts a minute per address, then
// 429 rate_limited. In memory per process. A singleton of the host's container rather than the framework's rate limiter
// middleware, whose limiters nothing disposes with the host: their replenishment timers kept every host this process
// ever started reachable (a test run starts one host per test). The container disposes this one with the host.
[RegisterSingleton]
public sealed class SignInRateLimiter : IDisposable
{
    public const int PerMinutePerIp = 30;

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(
        address => RateLimitPartition.GetFixedWindowLimiter(
            address,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = PerMinutePerIp, Window = TimeSpan.FromMinutes(1) }));

    // Throws rate_limited (429) once the request's address is over the limit.
    public void Acquire(HttpRequest request)
    {
        using var lease = _limiter.AttemptAcquire(ClientAddress(request));
        if (!lease.IsAcquired)
        {
            throw ApiException.RateLimited("Too many sign-in attempts. Wait a minute and try again.");
        }
    }

    public void Dispose() => _limiter.Dispose();

    // Cloudflare names the visitor; without it, the connection's own address.
    private static string ClientAddress(HttpRequest request)
        => request.Headers["CF-Connecting-IP"].FirstOrDefault()
           ?? request.HttpContext.Connection.RemoteIpAddress?.ToString()
           ?? "unknown";
}
