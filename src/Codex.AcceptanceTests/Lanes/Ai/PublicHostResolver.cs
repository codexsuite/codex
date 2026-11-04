using System.Net;
using Codex.Application.Network;

namespace Codex.AcceptanceTests.Lanes.Ai;

// Answers every host with a public address, so the base-URL guard lets https://gateway.test through without DNS.
public sealed class PublicHostResolver : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
}
