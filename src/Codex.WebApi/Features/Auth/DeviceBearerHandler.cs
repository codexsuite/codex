using System.Security.Claims;
using System.Text.Encodings.Web;
using Codex.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Codex.WebApi.Features.Auth;

// Authenticates a request carrying a device session secret (DeviceBearer). The principal has the session's user as
// NameIdentifier, so CurrentUser works unchanged, and the session id, so the list can mark the current device and
// DELETE /api/auth/device-sessions/current knows what to end. A secret that is unknown, revoked, expired, whose user is
// disabled or whose stamp has moved on fails the request, which the fallback policy turns into the usual 401.
public sealed class DeviceBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, DeviceSessions sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (DeviceBearer.SecretFrom(Request) is not { } secret)
        {
            return AuthenticateResult.NoResult();
        }
        if (await sessions.AuthenticateAsync(secret, Context.RequestAborted) is not { } grant)
        {
            return AuthenticateResult.Fail("The device session is not valid.");
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, grant.UserId.ToString()),
                new Claim(DeviceSessions.SessionClaim, grant.SessionId.ToString()),
            ],
            AuthServices.DeviceScheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthServices.DeviceScheme));
    }
}
