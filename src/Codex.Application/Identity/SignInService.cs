using Agoda.IoC.Core;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// POST /api/auth/login. Every refusal (unknown email, wrong password, no password, disabled, locked, too many attempts)
// is the same 401 with the same sentence, so the response never says whether an email has an account. Ten wrong
// passwords in a row lock the account for 15 minutes; an admin can unlock it sooner.
[RegisterSingleton]
public sealed class SignInService(NpgsqlDataSource dataSource, Passwords passwords, SignInAttempts attempts, UserSessions sessions)
{
    public const string Refusal = "Incorrect email or password, or the account is locked for now.";
    public const int LockoutFailures = 10;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private sealed record Candidate(Guid Id, string? PasswordHash, bool Disabled, bool Locked);

    public async Task<UserSnapshot> SignInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length == 0 || password.Length == 0 || !attempts.TryRecord(email))
        {
            throw ApiException.Unauthorized(Refusal);
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<Candidate>(new CommandDefinition(
            """
            SELECT id, password_hash AS PasswordHash, disabled_at IS NOT NULL AS Disabled,
                   coalesce(locked_until > now(), false) AS Locked
            FROM app_user WHERE lower(email) = lower(@email)
            """, new { email }, cancellationToken: cancellationToken));
        // Verify even when there is no user or no hash, so the refusal takes as long either way.
        var verified = passwords.Verify(user?.PasswordHash, password);
        if (user is null || user.Disabled || user.Locked)
        {
            throw ApiException.Unauthorized(Refusal);
        }
        if (!verified)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE app_user SET
                    locked_until = CASE WHEN failed_login_count + 1 >= @limit THEN now() + @lockout ELSE locked_until END,
                    failed_login_count = CASE WHEN failed_login_count + 1 >= @limit THEN 0 ELSE failed_login_count + 1 END
                WHERE id = @id
                """, new { id = user.Id, limit = LockoutFailures, lockout = Lockout }, cancellationToken: cancellationToken));
            throw ApiException.Unauthorized(Refusal);
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE app_user SET failed_login_count = 0, locked_until = NULL, last_login_at = now() WHERE id = @id",
            new { id = user.Id }, cancellationToken: cancellationToken));
        sessions.Forget(user.Id);
        return await sessions.GetAsync(user.Id, cancellationToken) ?? throw ApiException.Unauthorized(Refusal);
    }
}
