using System.Net;
using Codex.Application.Network;

namespace Codex.WebApi.Tests.AiIntegrations;

// IHostResolver double: every host resolves to one public address, except the hosts listed with their own.
public sealed class FixedHostResolver(Dictionary<string, string>? hosts = null) : IHostResolver
{
    public const string PublicAddress = "93.184.216.34";

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(new[] { IPAddress.Parse(hosts is not null && hosts.TryGetValue(host, out var own) ? own : PublicAddress) });
}
