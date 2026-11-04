using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbLinks
{
    Task LinkSceneAsync(Guid sceneId, Guid entryId, CancellationToken cancellationToken);

    Task UnlinkSceneAsync(Guid sceneId, Guid entryId, CancellationToken cancellationToken);

    // entryIds null is a 400, checked after the beat's ownership.
    Task SetBeatContextAsync(Guid beatId, IReadOnlyList<Guid>? entryIds, CancellationToken cancellationToken);
}

// Unknown scene, beat or entry is 404 not_found; an entry outside the book's scope (its own or its series') is
// 400 validation. The scope trigger stays the backstop, the check here gives the answer a clean error. A scene, beat or
// entry the current user cannot open is 404, like one that does not exist (#29); linking needs write on the scene's or
// beat's book and only view on the entry, which a link does not change (#80).
[RegisterSingleton]
public sealed class KbLinks(NpgsqlDataSource dataSource, IBookAccess access, ICurrentUser currentUser) : IKbLinks
{
    public async Task LinkSceneAsync(Guid sceneId, Guid entryId, CancellationToken cancellationToken)
    {
        var bookId = await SceneBookAsync(sceneId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireInScopeAsync(connection, transaction, bookId, [entryId], currentUser.Id, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@sceneId, @entryId)
                ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
                """,
                new { sceneId, entryId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw ApiException.NotFound($"Knowledge base entry {entryId} or scene {sceneId} was not found.");
        }
    }

    public async Task UnlinkSceneAsync(Guid sceneId, Guid entryId, CancellationToken cancellationToken)
    {
        await SceneBookAsync(sceneId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE scene_kb SET archived_at = now() WHERE scene_id = @sceneId AND kb_entry_id = @entryId AND archived_at IS NULL",
            new { sceneId, entryId }, cancellationToken: cancellationToken));
    }

    public async Task SetBeatContextAsync(Guid beatId, IReadOnlyList<Guid>? entryIds, CancellationToken cancellationToken)
    {
        var bookId = (await access.RequireAsync(BookItem.Beat, beatId, BookRole.Write, cancellationToken))!.Value;
        var ids = (entryIds ?? throw ApiException.Validation("entryIds is required.")).Distinct().ToArray();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireInScopeAsync(connection, transaction, bookId, ids, currentUser.Id, cancellationToken);
        // Entries dropped from the list are archived, listed ones added or revived (#85).
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE beat_kb SET archived_at = now() WHERE beat_id = @beatId AND kb_entry_id <> ALL(@ids) AND archived_at IS NULL",
            new { beatId, ids }, transaction, cancellationToken: cancellationToken));
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO beat_kb (beat_id, kb_entry_id) SELECT @beatId, unnest(@ids)
                ON CONFLICT (beat_id, kb_entry_id) DO UPDATE SET archived_at = NULL
                """,
                new { beatId, ids }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw ApiException.NotFound($"A knowledge base entry or beat {beatId} was not found.");
        }
    }

    // The scene's book, when the current user may write there ("Scene <id> was not found." without access).
    private async Task<Guid> SceneBookAsync(Guid sceneId, CancellationToken ct)
        => (await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, ct))!.Value;

    private static async Task RequireInScopeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId, Guid[] entryIds, Guid user, CancellationToken ct)
    {
        // FOR SHARE keeps an entry from being archived between this check and the insert. An entry the user cannot open
        // is not found; one they can open outside this book's scope is a validation error.
        var rows = (await connection.QueryAsync<(Guid Id, bool InScope)>(new CommandDefinition(
            """
            SELECT e.id, EXISTS (SELECT 1 FROM book_scope bs WHERE bs.book_id = @bookId AND bs.scope_id = e.book_id)
            FROM kb_entry e
            WHERE e.id = ANY(@entryIds) AND e.archived_at IS NULL
              AND EXISTS (SELECT 1 FROM book_access a WHERE a.book_id = e.book_id AND a.user_id = @user)
            FOR SHARE OF e
            """,
            new { bookId, entryIds, user }, transaction, cancellationToken: ct))).ToDictionary(r => r.Id, r => r.InScope);
        foreach (var id in entryIds)
        {
            if (!rows.TryGetValue(id, out var inScope))
            {
                throw ApiException.NotFound($"Knowledge base entry {id} was not found.");
            }
            if (!inScope)
            {
                throw ApiException.Validation($"Knowledge base entry {id} belongs to another book or series.");
            }
        }
    }
}
