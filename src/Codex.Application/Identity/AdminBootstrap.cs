using Codex.Contracts.Errors;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Identity;

// The first admin in production, where no password hash is committed: when CODEX_ADMIN_EMAIL and CODEX_ADMIN_PASSWORD
// are both set and that user has no password yet, set it and make the user an admin (creating the user if there is
// none). A user who already has a password is left alone, so leaving the variables set is harmless; remove them after
// the first boot anyway. Registered after the migration service (IdentityServices), so app_user has its columns.
public sealed class AdminBootstrap(NpgsqlDataSource dataSource, Passwords passwords, IConfiguration configuration, ILogger<AdminBootstrap> logger)
    : IHostedService
{
    public const string EmailKey = "CODEX_ADMIN_EMAIL";
    public const string PasswordKey = "CODEX_ADMIN_PASSWORD";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration[EmailKey]?.Trim();
        var password = configuration[PasswordKey];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return;
        }
        try
        {
            Passwords.RequireAcceptable(password);
        }
        catch (ApiException e)
        {
            throw new InvalidOperationException($"{PasswordKey}: {e.Message}", e);
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            WITH created AS (
                INSERT INTO app_user (email, display_name, password_hash, is_admin)
                SELECT @email, split_part(@email, '@', 1), @hash, true
                WHERE NOT EXISTS (SELECT 1 FROM app_user WHERE lower(email) = lower(@email))
                RETURNING id
            ), updated AS (
                UPDATE app_user SET password_hash = @hash, is_admin = true, security_stamp = gen_random_uuid(), updated_at = now()
                WHERE password_hash IS NULL AND lower(email) = lower(@email)
                RETURNING id
            )
            SELECT ((SELECT count(*) FROM created) + (SELECT count(*) FROM updated))::int
            """, new { email, hash = passwords.Hash(password) }, cancellationToken: cancellationToken));
        if (changed > 0)
        {
            logger.LogWarning("Set the admin password for {Email} from the environment; unset {EmailKey} and {PasswordKey} now.", email, EmailKey, PasswordKey);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
