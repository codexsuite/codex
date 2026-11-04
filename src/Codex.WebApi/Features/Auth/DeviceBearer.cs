using Codex.Application.Identity;
using Microsoft.Net.Http.Headers;

namespace Codex.WebApi.Features.Auth;

// Where a device session's secret is on a request, for the scheme selector and the handler alike: the Authorization
// bearer on /api and /hubs, or, on /hubs only, the access_token query value SignalR uses for WebSockets. Only a
// cdx_dev_ secret counts, so a personal access token on /api is simply not a device bearer (and stays 401), and /mcp is
// never looked at.
public static class DeviceBearer
{
    private const string BearerPrefix = "Bearer ";
    private const string HubQuery = "access_token";

    public static string? SecretFrom(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api") && !request.Path.StartsWithSegments("/hubs"))
        {
            return null;
        }
        var authorization = request.Headers[HeaderNames.Authorization].ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var secret = authorization[BearerPrefix.Length..].Trim();
            return IsDeviceSecret(secret) ? secret : null;
        }
        if (request.Path.StartsWithSegments("/hubs"))
        {
            var query = request.Query[HubQuery].ToString();
            return IsDeviceSecret(query) ? query : null;
        }
        return null;
    }

    public static bool Carries(HttpRequest request) => SecretFrom(request) is not null;

    private static bool IsDeviceSecret(string value) => value.StartsWith(DeviceSessions.SecretPrefix, StringComparison.Ordinal);
}
