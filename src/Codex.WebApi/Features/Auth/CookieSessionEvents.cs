using System.Security.Claims;
using Codex.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Codex.WebApi.Features.Auth;

// Checks each request's cookie against app_user (through the short UserSessions cache): a disabled user, or a cookie
// issued before the user's security stamp changed (password change, admin reset), is signed out. Requests to /api,
// /hubs and /mcp get 401/403 (ApiErrorServices writes the body) instead of the sign-in page redirect a page load gets.
public sealed class CookieSessionEvents(UserSessions sessions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var valid = Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                    && Guid.TryParse(principal?.FindFirstValue(AuthPrincipal.StampClaim), out var stamp)
                    && await sessions.GetAsync(userId, context.HttpContext.RequestAborted) is { Disabled: false } user
                    && user.SecurityStamp == stamp;
        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthServices.CookieScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
        => IsApi(context.Request) ? Status(context, StatusCodes.Status401Unauthorized) : base.RedirectToLogin(context);

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
        => Status(context, StatusCodes.Status403Forbidden);

    public static bool IsApi(HttpRequest request)
        => request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/hubs") || request.Path.StartsWithSegments("/mcp");

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
