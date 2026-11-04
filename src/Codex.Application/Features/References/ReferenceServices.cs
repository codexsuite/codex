using Codex.Application.Network;
using System.Net.Sockets;
using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.References;

// The named HttpClients behind HttpSourceLookup. Tests replace ISourceLookup, or stub these clients' handlers.
public sealed class ReferenceServices : IServiceModule
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // The API clients may follow a redirect, but every connection (each hop included) goes to a public address only.
        AddClient(services, HttpSourceLookup.OpenLibraryClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, ConnectCallback = ConnectToPublicAddressAsync });
        AddClient(services, HttpSourceLookup.CrossrefClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, ConnectCallback = ConnectToPublicAddressAsync });
        AddClient(services, HttpSourceLookup.WebClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = ConnectToPublicAddressAsync,
            });
    }

    private static IHttpClientBuilder AddClient(IServiceCollection services, string name) =>
        services.AddHttpClient(name, client =>
        {
            client.Timeout = LookupTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Codex/1.0 (source look-up)");
        });

    // Connects only to a public address, resolved here, so a host that changes its answer between the check and the connection gets nowhere.
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = (await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)).Where(PublicAddress.IsPublic).ToArray();
        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"{context.DnsEndPoint.Host} has no public address.");
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
