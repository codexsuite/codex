using System.Security.Cryptography;
using System.Text;
using Codex.Application.Identity;
using Dapper;
using Npgsql;

namespace Codex.TestSupport.Hosting;

// Personal access tokens (#32) written straight into a test database, for a test that needs one before any host runs
// (a stdio process started with CODEX_MCP_TOKEN) or with a state the Account page cannot make (already expired). Stored
// as ApiTokens stores them: the SHA-256 of the secret.
public static class TestTokens
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public sealed record Created(Guid Id, string Secret);

    public static async Task<Created> CreateAsync(
        string connectionString, Guid userId, string name, IReadOnlyList<string> scopes, DateTimeOffset? expiresAt = null)
    {
        var secret = ApiTokens.SecretPrefix + RandomNumberGenerator.GetString(Base62, 43);
        await using var connection = new NpgsqlConnection(connectionString);
        var id = await connection.QuerySingleAsync<Guid>(
            """
            INSERT INTO api_token (user_id, name, token_prefix, token_hash, scopes, expires_at)
            VALUES (@userId, @name, @prefix, @hash, @scopes, @expiresAt) RETURNING id
            """,
            new
            {
                userId, name, prefix = secret[..16], hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret)), scopes = scopes.ToArray(),
                expiresAt = expiresAt?.ToUniversalTime(),
            });
        return new Created(id, secret);
    }

    public static async Task RevokeAsync(string connectionString, Guid tokenId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("UPDATE api_token SET revoked_at = now() WHERE id = @tokenId", new { tokenId });
    }
}
