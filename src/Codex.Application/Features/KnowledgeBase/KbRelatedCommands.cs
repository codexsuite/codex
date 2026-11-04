using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbRelatedCommands
{
    Task<KbEntryDto> AddAsync(Guid entryId, Guid otherId, CancellationToken cancellationToken);

    Task<KbEntryDto> RemoveAsync(Guid entryId, Guid otherId, CancellationToken cancellationToken);
}

// "Related": one kb_entry_link row per pair, smaller id first. Unknown or deleted entry 404; linking an entry to
// itself, or to one outside the scope of this entry's book or series, 400 validation. Adding an existing pair and
// removing a missing one both succeed. Removing archives the row; adding the pair again revives it (#85).
[RegisterSingleton]
public sealed class KbRelatedCommands(NpgsqlDataSource dataSource, IKbEntryQueries queries, IBookEvents events, IBookAccess access) : IKbRelatedCommands
{
    private sealed record Pair(Guid Id, Guid BookId);

    public async Task<KbEntryDto> AddAsync(Guid entryId, Guid otherId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        await access.RequireAsync(BookItem.KbEntry, otherId, BookRole.View, cancellationToken);
        if (entryId == otherId)
        {
            throw ApiException.Validation("An entry cannot be related to itself.");
        }
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var pairs = (await connection.QueryAsync<Pair>(new CommandDefinition(
                "SELECT id, book_id AS BookId FROM kb_entry WHERE id = ANY(@ids) AND archived_at IS NULL ORDER BY id FOR SHARE",
                new { ids = new[] { entryId, otherId } }, transaction, cancellationToken: cancellationToken))).ToDictionary(p => p.Id);
            var entry = pairs.GetValueOrDefault(entryId) ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
            var other = pairs.GetValueOrDefault(otherId) ?? throw ApiException.NotFound($"Knowledge base entry {otherId} was not found.");
            ownerId = entry.BookId;
            // Either side's scope may hold the other (a book's entry and its series' entry).
            var inScope = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM book_scope WHERE book_id = @a AND scope_id = @b)
                    OR EXISTS (SELECT 1 FROM book_scope WHERE book_id = @b AND scope_id = @a)
                """,
                new { a = entry.BookId, b = other.BookId }, transaction, cancellationToken: cancellationToken));
            if (!inScope)
            {
                throw ApiException.Validation("That entry belongs to another book or series.");
            }
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
                VALUES (least(@entryId, @otherId), greatest(@entryId, @otherId))
                ON CONFLICT (entry_a_id, entry_b_id) DO UPDATE SET archived_at = NULL
                """,
                new { entryId, otherId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetAsync(entryId, cancellationToken);
    }

    public async Task<KbEntryDto> RemoveAsync(Guid entryId, Guid otherId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM kb_entry WHERE id = @entryId AND archived_at IS NULL", new { entryId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE kb_entry_link SET archived_at = now()
                WHERE entry_a_id = least(@entryId, @otherId) AND entry_b_id = greatest(@entryId, @otherId) AND archived_at IS NULL
                """,
                new { entryId, otherId }, cancellationToken: cancellationToken));
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetAsync(entryId, cancellationToken);
    }
}
