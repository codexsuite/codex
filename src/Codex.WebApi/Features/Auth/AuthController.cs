using Codex.Application.Identity;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Auth;

// Sign in, sign out, who am I, change my name or password (#29), my personal access tokens (#32) and my signed-in devices (#35). /login serves
// the SPA to anyone, so the sign-in page loads before there is a session (LoginPageEndpoints); every other page and /api route needs one
// (the fallback policy).
// POST   /api/auth/login                     LoginRequest -> MeDto (anonymous, rate limited; sets the session cookie)
// POST   /api/auth/logout                    -> 204 (anonymous; clears the session cookie)
// GET    /api/auth/me                        -> MeDto
// PATCH  /api/auth/me                        UpdateAccountRequest -> MeDto
// POST   /api/auth/password                  ChangePasswordRequest -> MeDto (fresh cookie; every other session ends)
// GET    /api/auth/tokens                    -> ApiTokenDto[]
// POST   /api/auth/tokens                    CreateApiTokenRequest -> 201 CreatedApiTokenDto
// DELETE /api/auth/tokens/{tokenId}          -> 204
// POST   /api/auth/device-sessions           CreateDeviceSessionRequest -> 201 CreatedDeviceSessionDto (anonymous, rate limited; no cookie)
// GET    /api/auth/device-sessions           -> DeviceSessionDto[]
// DELETE /api/auth/device-sessions/current   -> 204
// DELETE /api/auth/device-sessions/{sessionId} -> 204
[Route("api/auth")]
public sealed class AuthController(
    SignInService signIn,
    SignInRateLimiter signInLimiter,
    CookieSession cookie,
    AccountCommands account,
    ApiTokens tokens,
    DeviceSessions devices) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests)]
    public Task<MeDto> Login(LoginRequest request, CancellationToken ct)
    {
        signInLimiter.Acquire(Request);
        return WithCookie(signIn.SignInAsync(request, ct));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<NoContentResult> Logout() => NoContentAfter(cookie.SignOutAsync());

    [HttpGet("me")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public Task<MeDto> Me(CancellationToken ct) => account.MeAsync(ct);

    [HttpPatch("me")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<MeDto> UpdateMe(UpdateAccountRequest request, CancellationToken ct) => account.UpdateAsync(request, ct);

    // The new security stamp ends every other session; this one gets a fresh cookie under it.
    [HttpPost("password")]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<MeDto> ChangePassword(ChangePasswordRequest request, CancellationToken ct) => WithCookie(account.ChangePasswordAsync(request, ct));

    // Agent access (#32): personal access tokens for /mcp. Managed only from a signed-in session; /api never accepts
    // the tokens themselves.
    [HttpGet("tokens")]
    [ProducesResponseType<IReadOnlyList<ApiTokenDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ApiTokenDto>> ListTokens(CancellationToken ct) => tokens.ListAsync(ct);

    [HttpPost("tokens")]
    [ProducesResponseType<CreatedApiTokenDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<CreatedResult> CreateToken(CreateApiTokenRequest request, CancellationToken ct)
    {
        var created = await tokens.CreateAsync(request, ct);
        return Created($"/api/auth/tokens/{created.Token.Id}", created);
    }

    [HttpDelete("tokens/{tokenId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> RevokeToken(Guid tokenId, CancellationToken ct) => NoContentAfter(tokens.RevokeAsync(tokenId, ct));

    // Device sessions (#35 M1): the mobile app's bearer sign-in. No cookie is set; the secret is the credential on
    // /api and /hubs. "current" is the session the request was made with.
    [AllowAnonymous]
    [HttpPost("device-sessions")]
    [ProducesResponseType<CreatedDeviceSessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests)]
    public async Task<CreatedResult> CreateDeviceSession(CreateDeviceSessionRequest request, CancellationToken ct)
    {
        signInLimiter.Acquire(Request);
        var created = await devices.SignInAsync(request, ct);
        return Created($"/api/auth/device-sessions/{created.Session.Id}", created);
    }

    [HttpGet("device-sessions")]
    [ProducesResponseType<IReadOnlyList<DeviceSessionDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<DeviceSessionDto>> ListDeviceSessions(CancellationToken ct) => devices.ListAsync(ct);

    [HttpDelete("device-sessions/current")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> RevokeCurrentDeviceSession(CancellationToken ct) => NoContentAfter(devices.RevokeCurrentAsync(ct));

    [HttpDelete("device-sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> RevokeDeviceSession(Guid sessionId, CancellationToken ct) => NoContentAfter(devices.RevokeAsync(sessionId, ct));

    // Login and a password change both end in a session cookie for the user (a fresh one under the new stamp) and MeDto.
    private async Task<MeDto> WithCookie(Task<UserSnapshot> signingIn)
    {
        var user = await signingIn;
        await cookie.SignInAsync(user);
        return AccountCommands.ToMe(user);
    }
}
