using Microsoft.AspNetCore.Authorization;

namespace Codex.WebApi.Features.Auth;

// The Admin policy's requirement: the signed-in user is an active admin now (AdminAuthorizationHandler).
public sealed class AdminRequirement : IAuthorizationRequirement
{
}
