using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.Core;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.Application.Identity;

// Device sessions (#35 M1): the mobile app's bearer sign-in, listed under Account → Signed-in devices. The secret is
// cdx_dev_ plus 43 base62 characters; only its SHA-256 is stored, so it is shown once. A session works on /api and
// /hubs (never /mcp) until it is revoked, expires (90 days after its last use: each use slides expires_at on), its
// user is disabled, or the user's security stamp moves on (a password change or an admin reset, as for cookies).
[RegisterSingleton]
public sealed class DeviceSessions(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, SignInService signIn,
    IServiceProvider services, TimeProvider clock)
{
    public const string SecretPrefix = "cdx_dev_";
    public const int MaxName = 100;
    public const string SessionClaim = "codex:device-session";

    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int SecretLength = 43;
    private const int ShownCharacters = 8;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    // last_used_at (and with it the sliding expiry) is written at most this often per session, not on every request.
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private sealed record SessionRow(
        Guid Id, string Name, string TokenPrefix, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt);

    private const string SelectSessions =
        "SELECT id, name, token_prefix AS TokenPrefix, expires_at AS ExpiresAt, last_used_at AS LastUsedAt, created_at AS CreatedAt FROM device_session";

    // POST /api/auth/device-sessions: the /login checks (lockout, per-email limit, the one refusal) and then a session
    // instead of a cookie. The name is checked first, so a bad name costs no sign-in attempt.
    public async Task<CreatedDeviceSessionDto> SignInAsync(CreateDeviceSessionRequest request, CancellationToken cancellationToken)
    {
        var name = request.DeviceName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw ApiException.Validation("Give the device a name, such as \"JD's iPhone\".");
        }
        if (name.Length > MaxName)
        {
            throw ApiException.Validation($"The name can have at most {MaxName} characters.");
        }
        var user = await signIn.SignInAsync(new LoginRequest(request.Email, request.Password), cancellationToken);

        var secret = SecretPrefix + RandomNumberGenerator.GetString(Base62, SecretLength);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<SessionRow>(new CommandDefinition(
            """
            INSERT INTO device_session (user_id, name, token_prefix, token_hash, security_stamp, expires_at)
            VALUES (@userId, @name, @prefix, @hash, @stamp, now() + @lifetime)
            RETURNING id, name, token_prefix AS TokenPrefix, expires_at AS ExpiresAt, last_used_at AS LastUsedAt, created_at AS CreatedAt
            """,
            new { userId = user.Id, name, prefix = secret[..(SecretPrefix.Length + ShownCharacters)], hash = Hash(secret), stamp = user.SecurityStamp, lifetime = Lifetime },
            cancellationToken: cancellationToken));
        return new CreatedDeviceSessionDto(ToDto(row, current: true), secret, AccountCommands.ToMe(user));
    }

    // The signed-in user's sessions that are not revoked or expired, newest first.
    public async Task<IReadOnlyList<DeviceSessionDto>> ListAsync(CancellationToken cancellationToken)
    {
        var currentId = CurrentSessionId;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<SessionRow>(new CommandDefinition(
            $"{SelectSessions} WHERE user_id = @userId AND revoked_at IS NULL AND expires_at > now() ORDER BY created_at DESC, id DESC",
            new { userId = currentUser.Id }, cancellationToken: cancellationToken));
        return rows.Select(row => ToDto(row, row.Id == currentId)).ToList();
    }

    // Revokes one of the signed-in user's sessions; another user's session is not_found, as an unknown id is.
    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE device_session SET revoked_at = coalesce(revoked_at, now()) WHERE id = @sessionId AND user_id = @userId",
            new { sessionId, userId = currentUser.Id }, cancellationToken: cancellationToken));
        if (changed == 0)
        {
            throw ApiException.NotFound($"Device session {sessionId} was not found.");
        }
    }

    // The mobile app's Sign out: revokes the session this request is signed in with.
    public Task RevokeCurrentAsync(CancellationToken cancellationToken)
        => CurrentSessionId is { } id
            ? RevokeAsync(id, cancellationToken)
            : throw ApiException.NotFound("This request is not signed in with a device session.");

    // The session behind a bearer secret, or null when it is unknown, revoked or expired, its user is disabled, or the
    // user's security stamp has changed since sign-in. A use slides the expiry on (at most once a minute).
    public async Task<DeviceSessionGrant?> AuthenticateAsync(string? secret, CancellationToken cancellationToken)
    {
        if (secret is null || !secret.StartsWith(SecretPrefix, StringComparison.Ordinal) || secret.Length != SecretPrefix.Length + SecretLength)
        {
            return null;
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DeviceSessionGrant>(new CommandDefinition(
            """
            WITH hit AS (
                SELECT d.id, d.user_id, d.last_used_at
                FROM device_session d JOIN app_user u ON u.id = d.user_id
                WHERE d.token_hash = @hash AND d.revoked_at IS NULL AND d.expires_at > now()
                  AND u.disabled_at IS NULL AND u.security_stamp = d.security_stamp
            ), touched AS (
                UPDATE device_session SET last_used_at = now(), expires_at = now() + @lifetime
                WHERE id IN (SELECT id FROM hit WHERE last_used_at IS NULL OR last_used_at < now() - @touch)
            )
            SELECT id AS SessionId, user_id AS UserId FROM hit
            """,
            new { hash = Hash(secret), lifetime = Lifetime, touch = TouchInterval }, cancellationToken: cancellationToken));
    }

    private Guid? CurrentSessionId
        => Guid.TryParse(services.GetService<IHttpContextAccessor>()?.HttpContext?.User.FindFirstValue(SessionClaim), out var id) ? id : null;

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private DeviceSessionDto ToDto(SessionRow row, bool current) => new(
        row.Id, row.Name, row.TokenPrefix, current,
        row.ExpiresAt, DisplayDate.ShortDay(row.ExpiresAt, clock.GetUtcNow(), clock.LocalTimeZone),
        row.LastUsedAt, row.LastUsedAt is { } used ? DisplayDate.Relative(used, clock) : null,
        row.CreatedAt, DisplayDate.Relative(row.CreatedAt, clock));
}
