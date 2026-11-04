using System.Net;
using Microsoft.AspNetCore.Http;

namespace Codex.Application.Mcp;

// "Bound to localhost" for an endpoint on a host that listens more widely (M7): who may use /mcp without a token, in
// Development (McpHttpEndpoints). A request counts as local only when it comes from a loopback address, names a
// loopback host (so a page on another site cannot reach it by DNS rebinding), carries no Origin other than this server
// itself on a loopback host (a browser page elsewhere, or the opaque "null" origin, is refused), and did not come
// through a proxy (a reverse proxy or tunnel on the same machine would otherwise make a remote caller look local).
public static class McpLoopback
{
    private static readonly string[] ProxyHeaders =
        ["Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Real-IP", "CF-Connecting-IP", "True-Client-IP", "Via"];

    public static bool Allows(ConnectionInfo connection, HttpRequest request)
        => connection.RemoteIpAddress is { } remote && IsLoopback(remote)
               && IsLoopbackHost(request.Host.Host)
               && (!request.Headers.TryGetValue("Origin", out var origin) || IsOwnOrigin(origin.ToString(), connection.LocalPort))
               && !ProxyHeaders.Any(request.Headers.ContainsKey);

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLoopback(address));

    // http://localhost:<the port this request came in on>, or the same on a loopback address.
    private static bool IsOwnOrigin(string origin, int localPort) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && IsLoopbackHost(uri.Host)
        && uri.Port == localPort;
}
