using System.Net;
using Agoda.IoC.Core;
using Codex.Application.Network;
using Codex.Contracts.Errors;
using Microsoft.Extensions.Configuration;

namespace Codex.Application.Features.Ai.Integrations;

// IAiEndpointGuard: the SSRF check on a user-supplied base URL. https only and public addresses only, unless the
// server owner has set CODEX_AI_ALLOW_LOCAL_ENDPOINTS (read on every call, so a change needs no restart). A URL with
// credentials, a query or a fragment is refused either way. Every address the host resolves to must be allowed
// (IsAllowedAddress): one refused answer is enough. Link-local addresses (cloud metadata) are refused even with the
// switch on. The AI HttpClients apply the same rule again when they connect, so a host that changes its DNS answer
// between this check and the connection gets nowhere.
[RegisterSingleton]
public sealed class AiEndpointGuard(IConfiguration configuration, IHostResolver resolver) : IAiEndpointGuard
{
    public bool LocalEndpointsAllowed =>
        string.Equals(configuration[AiSettings.AllowLocalEndpointsKey]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public async Task RequireAllowedAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        var local = LocalEndpointsAllowed;
        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttps && !(local && baseUrl.Scheme == Uri.UriSchemeHttp)))
        {
            throw ApiException.Validation("Use an https:// address.");
        }

        if (baseUrl.UserInfo.Length > 0 || baseUrl.Query.Length > 0 || baseUrl.Fragment.Length > 0)
        {
            throw ApiException.Validation("The address can't contain a user name, password, query or #fragment.");
        }

        var host = baseUrl.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await ResolveAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(a => !IsAllowedAddress(a, local)))
        {
            throw ApiException.Validation(local
                ? $"{host} is a link-local address, which is never allowed."
                : $"{host} is not on the public internet. The server owner can allow local endpoints with {AiSettings.AllowLocalEndpointsKey}.");
        }
    }

    // Public addresses always; with local endpoints allowed, private and loopback ones too, but never link-local
    // (169.254.0.0/16, fe80::/10), where cloud metadata services answer.
    public static bool IsAllowedAddress(IPAddress address, bool localAllowed)
    {
        if (PublicAddress.IsPublic(address))
        {
            return true;
        }

        if (!localAllowed)
        {
            return false;
        }

        var v4 = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return !(v4.IsIPv6LinkLocal || (v4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && v4.GetAddressBytes() is [169, 254, _, _]));
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await resolver.ResolveAsync(host, cancellationToken);
        }
        catch (System.Net.Sockets.SocketException)
        {
            throw ApiException.Validation($"Couldn't find {host}. Check the address.");
        }
    }
}
