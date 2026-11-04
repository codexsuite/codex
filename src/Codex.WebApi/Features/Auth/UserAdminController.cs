using Codex.Application.Identity;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Auth;

// The user management page's API (/settings/users), admins only: anyone else gets 403.
// GET  /api/admin/users                              -> AdminUserDto[]
// POST /api/admin/users                              CreateUserRequest -> 201 OneTimePasswordDto
// PATCH /api/admin/users/{userId}                    UpdateUserRequest -> AdminUserDto
// POST /api/admin/users/{userId}/reset-password      -> OneTimePasswordDto
// POST /api/admin/users/{userId}/revoke-tokens       -> 204 (every personal access token the user has, #32)
[Authorize(Policy = AuthServices.AdminPolicy)]
[Route("api/admin/users")]
public sealed class UserAdminController(UserAdminCommands admin, ApiTokens tokens) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<AdminUserDto>> List(CancellationToken ct) => admin.ListAsync(ct);

    [HttpPost]
    [ProducesResponseType<OneTimePasswordDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await admin.CreateAsync(request, ct);
        return Created($"/api/admin/users/{created.User.Id}", created);
    }

    [HttpPatch("{userId:guid}")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<AdminUserDto> Update(Guid userId, UpdateUserRequest request, CancellationToken ct) => admin.UpdateAsync(userId, request, ct);

    [HttpPost("{userId:guid}/reset-password")]
    [ProducesResponseType<OneTimePasswordDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OneTimePasswordDto> ResetPassword(Guid userId, CancellationToken ct) => admin.ResetPasswordAsync(userId, ct);

    [HttpPost("{userId:guid}/revoke-tokens")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> RevokeTokens(Guid userId, CancellationToken ct) => NoContentAfter(tokens.RevokeAllAsync(userId, ct));
}
