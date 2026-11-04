using System.Collections.Concurrent;
using Agoda.IoC.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Identity;

// app_user as each request sees it, cached briefly so a signed-in page does not cost a query per request. Anything that
// changes a user (disable, reset, admin flag, password change) calls Forget, so this process sees the change on the
// next request; the cache only ever delays changes made by another process, and by at most Lifetime.
[RegisterSingleton]
public sealed class UserSessions(NpgsqlDataSource dataSource, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private sealed record Entry(UserSnapshot? User, DateTimeOffset ReadAt);

    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();

    // Null when no such user exists.
    public async Task<UserSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(userId, out var cached) && now - cached.ReadAt < Lifetime)
        {
            return cached.User;
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserSnapshot>(new CommandDefinition(
            """
            SELECT id, email, display_name AS DisplayName, is_admin AS IsAdmin, must_change_password AS MustChangePassword,
                   disabled_at IS NOT NULL AS Disabled, security_stamp AS SecurityStamp
            FROM app_user WHERE id = @userId
            """, new { userId }, cancellationToken: cancellationToken));
        _cache[userId] = new Entry(user, now);
        return user;
    }

    public void Forget(Guid userId) => _cache.TryRemove(userId, out _);
}
