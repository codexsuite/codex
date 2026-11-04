using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// The signed-in user's own account: who they are (GET /api/auth/me), their display name and their password.
[RegisterSingleton]
public sealed class AccountCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, UserSessions sessions, Passwords passwords)
{
    public const int MaxDisplayName = 100;

    public async Task<MeDto> MeAsync(CancellationToken cancellationToken)
        => ToMe(await sessions.GetAsync(currentUser.Id, cancellationToken)
                ?? throw ApiException.Unauthorized("Sign in to continue."));

    public static MeDto ToMe(UserSnapshot user)
        => new(user.Id, user.Email, user.DisplayName, UserInitials.From(user.DisplayName), user.IsAdmin, user.MustChangePassword);

    public async Task<MeDto> UpdateAsync(UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var displayName = RequireDisplayName(request.DisplayName);
        var userId = currentUser.Id;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE app_user SET display_name = @displayName, updated_at = now() WHERE id = @userId",
                new { userId, displayName }, cancellationToken: cancellationToken));
        }
        sessions.Forget(userId);
        return await MeAsync(cancellationToken);
    }

    // Changes the password and the security stamp, which ends every other session; the caller re-issues this session's
    // cookie with the returned user's new stamp.
    public async Task<UserSnapshot> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var hash = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT password_hash FROM app_user WHERE id = @userId", new { userId }, cancellationToken: cancellationToken));
            // validation, not unauthorized: a 401 would send the page to the sign-in screen.
            if (!passwords.Verify(hash, request.CurrentPassword ?? string.Empty))
            {
                throw ApiException.Validation("The current password is not right.");
            }
            Passwords.RequireAcceptable(request.NewPassword);
            if (request.NewPassword == request.CurrentPassword)
            {
                throw ApiException.Validation("Choose a new password that differs from the current one.");
            }
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE app_user SET password_hash = @hash, must_change_password = @mustChange, security_stamp = gen_random_uuid(),
                                    failed_login_count = 0, locked_until = NULL, updated_at = now()
                WHERE id = @userId
                """, new { userId, hash = passwords.Hash(request.NewPassword!), mustChange = false }, cancellationToken: cancellationToken));
        }
        sessions.Forget(userId);
        return await sessions.GetAsync(userId, cancellationToken) ?? throw ApiException.Unauthorized("Sign in to continue.");
    }

    public static string RequireDisplayName(string? value)
    {
        var displayName = value?.Trim() ?? string.Empty;
        if (displayName.Length == 0)
        {
            throw ApiException.Validation("Display name is required.");
        }
        return displayName.Length > MaxDisplayName
            ? throw ApiException.Validation($"Display name can have at most {MaxDisplayName} characters.")
            : displayName;
    }
}
