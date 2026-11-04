using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// The user management page (/settings/users, admins only): list users, add one, reset a password, disable or enable,
// make or remove admin, unlock. There is no sign-up and no hard delete (a user owns books). The guards are enforced
// here, not in the UI: an admin cannot disable themselves or remove their own admin rights, and at least one active
// admin always remains.
[RegisterSingleton]
public sealed class UserAdminCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, UserSessions sessions, Passwords passwords, TimeProvider clock)
{
    public const int MaxEmail = 254;

    private sealed record UserRow(
        Guid Id, string Email, string DisplayName, bool IsAdmin, bool Disabled, bool Locked, bool MustChangePassword,
        DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, int TokenCount);

    private const string SelectUsers =
        """
        SELECT id, email, display_name AS DisplayName, is_admin AS IsAdmin, disabled_at IS NOT NULL AS Disabled,
               coalesce(locked_until > now(), false) AS Locked, must_change_password AS MustChangePassword,
               last_login_at AS LastLoginAt, created_at AS CreatedAt,
               (SELECT count(*)::int FROM api_token t
                WHERE t.user_id = app_user.id AND t.revoked_at IS NULL AND (t.expires_at IS NULL OR t.expires_at > now())) AS TokenCount
        FROM app_user
        """;

    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            $"{SelectUsers} ORDER BY lower(display_name), lower(email)", cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToList();
    }

    public async Task<OneTimePasswordDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = RequireEmail(request.Email);
        var displayName = AccountCommands.RequireDisplayName(request.DisplayName);
        var oneTime = Passwords.OneTime();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var id = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            INSERT INTO app_user (email, display_name, password_hash, must_change_password)
            SELECT @email, @displayName, @hash, true
            WHERE NOT EXISTS (SELECT 1 FROM app_user WHERE lower(email) = lower(@email))
            RETURNING id
            """, new { email, displayName, hash = passwords.Hash(oneTime) }, cancellationToken: cancellationToken))
            ?? throw ApiException.Conflict($"A user with the email {email} already exists.");
        return new OneTimePasswordDto(await GetAsync(connection, id, cancellationToken), oneTime);
    }

    public async Task<AdminUserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var self = currentUser.Id;
        if (userId == self && request.Disabled == true)
        {
            throw ApiException.Conflict("You cannot disable your own account.");
        }
        if (userId == self && request.IsAdmin == false)
        {
            throw ApiException.Conflict("You cannot remove your own admin rights.");
        }
        AdminUserDto user;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // Lock every admin row, so two admins demoting each other at once cannot leave none.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT id FROM app_user WHERE is_admin FOR UPDATE", transaction: transaction, cancellationToken: cancellationToken));
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE app_user SET
                    is_admin = coalesce(@isAdmin::boolean, is_admin),
                    disabled_at = CASE WHEN @disabled::boolean IS NULL THEN disabled_at
                                       WHEN @disabled::boolean THEN coalesce(disabled_at, now()) ELSE NULL END,
                    locked_until = CASE WHEN @unlock THEN NULL ELSE locked_until END,
                    failed_login_count = CASE WHEN @unlock THEN 0 ELSE failed_login_count END,
                    updated_at = now()
                WHERE id = @userId
                """, new { userId, isAdmin = request.IsAdmin, disabled = request.Disabled, unlock = request.Unlock == true },
                transaction, cancellationToken: cancellationToken));
            if (changed == 0)
            {
                throw ApiException.NotFound($"User {userId} was not found.");
            }
            var activeAdmins = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*)::int FROM app_user WHERE is_admin AND disabled_at IS NULL",
                transaction: transaction, cancellationToken: cancellationToken));
            if (activeAdmins == 0)
            {
                throw ApiException.Conflict("Codex needs at least one active admin.");
            }
            user = await GetAsync(connection, userId, cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
        }
        sessions.Forget(userId);
        return user;
    }

    // A new one-time password; the new security stamp signs the user out everywhere.
    public async Task<OneTimePasswordDto> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var oneTime = Passwords.OneTime();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE app_user SET password_hash = @hash, must_change_password = @mustChange, security_stamp = gen_random_uuid(),
                                failed_login_count = 0, locked_until = NULL, updated_at = now()
            WHERE id = @userId
            """, new { userId, hash = passwords.Hash(oneTime), mustChange = true }, cancellationToken: cancellationToken));
        if (changed == 0)
        {
            throw ApiException.NotFound($"User {userId} was not found.");
        }
        sessions.Forget(userId);
        return new OneTimePasswordDto(await GetAsync(connection, userId, cancellationToken), oneTime);
    }

    private async Task<AdminUserDto> GetAsync(
        NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
        => ToDto(await connection.QuerySingleAsync<UserRow>(new CommandDefinition(
            $"{SelectUsers} WHERE id = @userId", new { userId }, transaction, cancellationToken: cancellationToken)));

    private AdminUserDto ToDto(UserRow row) => new(
        row.Id, row.Email, row.DisplayName, row.IsAdmin,
        Status(row),
        row.MustChangePassword,
        row.LastLoginAt, row.LastLoginAt is { } last ? DisplayDate.Relative(last, clock) : null,
        row.CreatedAt, DisplayDate.Relative(row.CreatedAt, clock),
        row.TokenCount);

    private static string Status(UserRow row)
    {
        if (row.Disabled)
        {
            return "disabled";
        }
        return row.Locked ? "locked" : "active";
    }

    private static string RequireEmail(string? value)
    {
        var email = value?.Trim() ?? string.Empty;
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || email.Any(char.IsWhiteSpace))
        {
            throw ApiException.Validation("Enter an email address such as name@example.com.");
        }
        return email.Length > MaxEmail ? throw ApiException.Validation($"The email can have at most {MaxEmail} characters.") : email;
    }
}
