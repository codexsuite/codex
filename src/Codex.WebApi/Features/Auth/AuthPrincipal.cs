using System.Security.Claims;
using Codex.Application.Identity;

namespace Codex.WebApi.Features.Auth;

// The cookie's principal: the user id (NameIdentifier, which CurrentUser reads) and the security stamp it was issued
// under (CookieSessionEvents ends the session once app_user's stamp moves on).
public static class AuthPrincipal
{
    public const string StampClaim = "codex:stamp";

    public static ClaimsPrincipal For(UserSnapshot user) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(StampClaim, user.SecurityStamp.ToString()),
        ],
        AuthServices.CookieScheme));
}
