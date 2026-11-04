using System.Net;
using Codex.Application.Network;

namespace Codex.UnitTest.Contracts;

// Resolves every host to one address, except the hosts listed with their own.
public sealed class FixedHostResolver(string defaultAddress, Dictionary<string, string>? hosts = null) : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(new[] { IPAddress.Parse(hosts is not null && hosts.TryGetValue(host, out var own) ? own : defaultAddress) });
}
