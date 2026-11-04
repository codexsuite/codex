using System.Security.Claims;
using Codex.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Codex.WebApi.Features.Auth;

// Reads is_admin from app_user (through the short UserSessions cache) rather than from a claim, so removing someone's
// admin rights takes effect on their next request.
public sealed class AdminAuthorizationHandler(UserSessions sessions) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await sessions.GetAsync(userId) is { IsAdmin: true, Disabled: false })
        {
            context.Succeed(requirement);
        }
    }
}
