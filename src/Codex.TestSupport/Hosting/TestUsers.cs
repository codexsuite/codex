using Codex.Application.Identity;
using Dapper;
using Npgsql;

namespace Codex.TestSupport.Hosting;

// Users a test needs beyond the seeded JD (whose password is db/seed/08_auth.sql's dev password, docs/auth.md).
public static class TestUsers
{
    public const string JdEmail = "jd@codex.local";
    public const string DevPassword = "codex-dev-password";

    // A user with an empty library; password (if given) lets them sign in through /api/auth/login.
    public static async Task<Guid> CreateAsync(
        string connectionString, string email, string displayName, string? password = null, bool isAdmin = false, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.QuerySingleAsync<Guid>(
            """
            INSERT INTO app_user (id, email, display_name, password_hash, is_admin)
            VALUES (coalesce(@id, uuidv7()), @email, @displayName, @hash, @isAdmin) RETURNING id
            """,
            new { id, email, displayName, hash = password is null ? null : new Passwords().Hash(password), isAdmin });
    }
}
