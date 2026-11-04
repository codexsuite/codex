using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// Personal access tokens for MCP (#32 phase 1): the Account page's Agent access list (create, list, revoke), an
// admin's revoke-all, and checking a bearer token on /mcp. The secret is cdx_pat_ plus 43 base62 characters (as much
// randomness as 32 bytes); only its SHA-256 is stored, so it is shown once. A token works until it is revoked or
// expires, and only while its user is not disabled.
[RegisterSingleton]
public sealed class ApiTokens(NpgsqlDataSource dataSource, ICurrentUser currentUser, TimeProvider clock)
{
    public const string SecretPrefix = "cdx_pat_";
    public const int MaxName = 100;
    public const int MaxExpiresInDays = 3650;

    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int SecretLength = 43;
    private const int ShownCharacters = 8;

    // last_used_at is written at most this often per token, not on every request.
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    // Scopes arrive as JSON: Dapper can't bind a text[] to a record.
    private sealed record TokenRow(
        Guid Id, string Name, string TokenPrefix, string ScopesJson, DateTimeOffset? ExpiresAt, bool Expired,
        DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt);

    private sealed record GrantRow(Guid TokenId, string Name, Guid UserId, string ScopesJson)
    {
        public ApiTokenGrant ToGrant() => new(TokenId, Name, UserId, Scopes(ScopesJson));
    }

    private const string SelectTokens =
        """
        SELECT id, name, token_prefix AS TokenPrefix, to_json(scopes)::text AS ScopesJson, expires_at AS ExpiresAt,
               coalesce(expires_at <= now(), false) AS Expired, last_used_at AS LastUsedAt, created_at AS CreatedAt
        FROM api_token
        """;

    // The signed-in user's tokens that have not been revoked, newest first; expired ones stay listed.
    public async Task<IReadOnlyList<ApiTokenDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TokenRow>(new CommandDefinition(
            $"{SelectTokens} WHERE user_id = @userId AND revoked_at IS NULL ORDER BY created_at DESC, id DESC",
            new { userId = currentUser.Id }, cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToList();
    }

    public async Task<CreatedApiTokenDto> CreateAsync(CreateApiTokenRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw ApiException.Validation("Give the token a name, such as \"Claude Code on the desktop\".");
        }
        if (name.Length > MaxName)
        {
            throw ApiException.Validation($"The name can have at most {MaxName} characters.");
        }
        var scopes = RequireScopes(request.Scopes);
        if (request.ExpiresInDays is < 1 or > MaxExpiresInDays)
        {
            throw ApiException.Validation($"A token can last from 1 to {MaxExpiresInDays} days, or never expire.");
        }

        var secret = SecretPrefix + RandomNumberGenerator.GetString(Base62, SecretLength);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<TokenRow>(new CommandDefinition(
            """
            INSERT INTO api_token (user_id, name, token_prefix, token_hash, scopes, expires_at)
            VALUES (@userId, @name, @prefix, @hash, @scopes, now() + make_interval(days => @days::int))
            RETURNING id, name, token_prefix AS TokenPrefix, to_json(scopes)::text AS ScopesJson, expires_at AS ExpiresAt, false AS Expired,
                      last_used_at AS LastUsedAt, created_at AS CreatedAt
            """,
            new
            {
                userId = currentUser.Id, name, prefix = secret[..(SecretPrefix.Length + ShownCharacters)], hash = Hash(secret),
                scopes, days = request.ExpiresInDays,
            },
            cancellationToken: cancellationToken));
        return new CreatedApiTokenDto(ToDto(row), secret);
    }

    // Revokes one of the signed-in user's tokens; another user's token is not_found, as an unknown id is.
    public async Task RevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE api_token SET revoked_at = coalesce(revoked_at, now()) WHERE id = @tokenId AND user_id = @userId",
            new { tokenId, userId = currentUser.Id }, cancellationToken: cancellationToken));
        if (changed == 0)
        {
            throw ApiException.NotFound($"Token {tokenId} was not found.");
        }
    }

    // An admin revoking every token a user has (the Users page).
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            WITH revoked AS (UPDATE api_token SET revoked_at = now() WHERE user_id = @userId AND revoked_at IS NULL)
            SELECT EXISTS (SELECT 1 FROM app_user WHERE id = @userId)
            """,
            new { userId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound($"User {userId} was not found.");
        }
    }

    // The token behind a bearer secret, or null when it is unknown, revoked or expired, or its user is disabled.
    public async Task<ApiTokenGrant?> AuthenticateAsync(string? secret, CancellationToken cancellationToken)
    {
        if (!IsWellFormed(secret))
        {
            return null;
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<GrantRow>(GrantQuery(secret, cancellationToken)))?.ToGrant();
    }

    // The same check made synchronously, for McpUser's first-use lookup of CODEX_MCP_TOKEN.
    public static ApiTokenGrant? Find(NpgsqlConnection connection, string? secret)
    {
        if (!IsWellFormed(secret))
        {
            return null;
        }
        return connection.QuerySingleOrDefault<GrantRow>(GrantQuery(secret, CancellationToken.None))?.ToGrant();
    }

    private static bool IsWellFormed([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? secret) =>
        secret is not null && secret.StartsWith(SecretPrefix, StringComparison.Ordinal) && secret.Length == SecretPrefix.Length + SecretLength;

    private static CommandDefinition GrantQuery(string secret, CancellationToken cancellationToken) => new(
        """
        WITH hit AS (
            SELECT t.id, t.name, t.user_id, t.scopes, t.last_used_at
            FROM api_token t JOIN app_user u ON u.id = t.user_id
            WHERE t.token_hash = @hash AND t.revoked_at IS NULL AND (t.expires_at IS NULL OR t.expires_at > now())
              AND u.disabled_at IS NULL
        ), touched AS (
            UPDATE api_token SET last_used_at = now()
            WHERE id IN (SELECT id FROM hit WHERE last_used_at IS NULL OR last_used_at < now() - @touch)
        )
        SELECT id AS TokenId, name, user_id AS UserId, to_json(scopes)::text AS ScopesJson FROM hit
        """,
        new { hash = Hash(secret), touch = TouchInterval }, cancellationToken: cancellationToken);

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private static string[] Scopes(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];

    // Known scopes only, codex:read always, in ApiTokenScopes.All's order.
    private static string[] RequireScopes(IReadOnlyList<string>? requested)
    {
        var asked = requested ?? [];
        var unknown = asked.Where(s => !ApiTokenScopes.All.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            throw ApiException.Validation($"Unknown scope {unknown[0]}: use {string.Join(", ", ApiTokenScopes.All)}.");
        }
        return ApiTokenScopes.All.Where(s => s == ApiTokenScopes.Read || asked.Contains(s)).ToArray();
    }

    private ApiTokenDto ToDto(TokenRow row)
    {
        var now = clock.GetUtcNow();
        return new ApiTokenDto(
            row.Id, row.Name, row.TokenPrefix, Scopes(row.ScopesJson),
            row.ExpiresAt, row.ExpiresAt is { } expires ? DisplayDate.ShortDay(expires, now, clock.LocalTimeZone) : null, row.Expired,
            row.LastUsedAt, row.LastUsedAt is { } used ? DisplayDate.Relative(used, clock) : null,
            row.CreatedAt, DisplayDate.Relative(row.CreatedAt, clock));
    }
}
