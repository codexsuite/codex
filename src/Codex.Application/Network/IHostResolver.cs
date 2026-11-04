using System.Net;
using Agoda.IoC.Core;

namespace Codex.Application.Network;

// Resolves a host name to its addresses; a seam so the look-up's private-host check can be tested without DNS.
public interface IHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class DnsHostResolver : IHostResolver
{
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
