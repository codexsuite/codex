using Codex.Application.Features.Ai.Integrations;
using System.Net;
using System.Net.Sockets;
using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai.Providers;

// Registers IAiProviders and the two Chat Completions named clients. Redirects are never followed: a 3xx from a
// user-supplied host could point past the SSRF guard, so it is treated as a request the provider refused. Every
// connection is made to an address AiEndpointGuard.IsAllowedAddress accepts, resolved at connect time, so a host that
// changes its DNS answer after the guard's check gets nowhere. Header timeout only; the stream has its own idle limit.
// The `anthropic` client is registered by AiServices.
public sealed class AiProviderServices : IServiceModule
{
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(120);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // The user-supplied host may be local only if the server owner says so (read when the handler is built); OpenRouter is a fixed public host.
        services.AddHttpClient(AiHttpClients.OpenAiCompatible, client => client.Timeout = HeaderTimeout)
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(
                string.Equals(sp.GetRequiredService<IConfiguration>()[AiSettings.AllowLocalEndpointsKey]?.Trim(), "true", StringComparison.OrdinalIgnoreCase)));
        services.AddHttpClient(AiHttpClients.OpenRouter, client => client.Timeout = HeaderTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(localAllowed: false));

        // A factory: AiProviders' optional ChatCompletionsLimits is for tests and is not a registered service.
        services.AddSingleton<IAiProviders>(sp => new AiProviders(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IAiEndpointGuard>(), sp.GetRequiredService<ILoggerFactory>()));
    }

    // The production handler for the Chat Completions clients: no redirects, no proxy (a proxy would resolve the host
    // itself), and a connect step that re-checks every address.
    public static SocketsHttpHandler CreateHandler(bool localAllowed) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = (context, cancellationToken) => ConnectToAllowedAddressAsync(context, localAllowed, cancellationToken),
    };

    private static async ValueTask<Stream> ConnectToAllowedAddressAsync(SocketsHttpConnectionContext context, bool localAllowed, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken)).Where(a => AiEndpointGuard.IsAllowedAddress(a, localAllowed)).ToArray();
        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"{host} has no address this app may connect to.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
