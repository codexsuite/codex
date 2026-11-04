using Agoda.IoC.Core;
using Codex.Contracts.Events;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Sharing;

// IAccessChanges over book_access: a before/after diff per (book, user). The capture runs before the command's
// transaction, so a concurrent grant change can make one event redundant, never wrong: each event carries the role
// read back after the commit. The owner is left out: ownership never changes here, and a deleted book or series tells
// its owner through BookMetaChanged.
[RegisterSingleton]
public sealed class AccessChanges(NpgsqlDataSource dataSource, IBookEvents events) : IAccessChanges
{
    private sealed record Row(Guid BookId, Guid UserId, string Role);

    public async Task<AccessSnapshot> CaptureAsync(
        IReadOnlyCollection<Guid> bookIds, Guid? userId, CancellationToken cancellationToken = default)
    {
        var ids = bookIds.Distinct().ToArray();
        return new AccessSnapshot(ids, userId, await ReadAsync(ids, userId, cancellationToken));
    }

    public async Task PublishAsync(AccessSnapshot before, CancellationToken cancellationToken = default)
    {
        var after = await ReadAsync(before.BookIds.ToArray(), before.UserId, cancellationToken);
        var changed = before.Roles.Keys.Union(after.Keys)
            .Select(key => (key, Role: after.GetValueOrDefault(key)))
            .Where(x => x.Role != before.Roles.GetValueOrDefault(x.key))
            .OrderBy(x => x.key.BookId).ThenBy(x => x.key.UserId);
        foreach (var (key, role) in changed)
        {
            await events.PublishAsync(new AccessChanged(key.BookId, key.UserId, role));
        }
    }

    private async Task<Dictionary<(Guid BookId, Guid UserId), string>> ReadAsync(
        Guid[] bookIds, Guid? userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT book_id AS BookId, user_id AS UserId, role AS Role FROM book_access
            WHERE book_id = ANY(@bookIds) AND (@userId::uuid IS NULL OR user_id = @userId) AND role <> 'owner'
            """,
            new { bookIds, userId }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => (r.BookId, r.UserId), r => r.Role);
    }
}
