using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Library;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Library;

// Deleting a book for good. Only an archived book can go (409 otherwise) and there is no trash period. From the user's
// side it is gone: deleted_at hides it everywhere, the Archived view included, and its id is a 404 (#85: app code never
// hard-deletes, so the rows and stored files stay). The Library asks for the typed title; the server only checks the
// archive rule. Owner only (#80; the owner_id filters stay on purpose).
[RegisterSingleton]
public sealed class BookDeleteCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events)
{
    public async Task<BookDeleteImpactDto> ImpactAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, notFound: "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<BookDeleteImpactDto>(new CommandDefinition(
            """
            SELECT b.id AS BookId, b.title, (b.archived_at IS NOT NULL) AS Archived,
                   (SELECT count(*) FROM chapter c WHERE c.book_id = b.id AND c.archived_at IS NULL)::int AS Chapters,
                   (SELECT count(*) FROM scene_stats ss WHERE ss.book_id = b.id)::int AS Scenes,
                   (SELECT coalesce(sum(ss.word_count), 0) FROM scene_stats ss WHERE ss.book_id = b.id)::int AS Words,
                   (SELECT count(*) FROM reference r WHERE r.book_id = b.id AND r.archived_at IS NULL)::int AS Sources
            FROM book b WHERE b.id = @bookId AND b.kind = 'book' AND b.owner_id = @owner
            """,
            new { bookId, owner = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("That book doesn't exist.");
    }

    public async Task DeleteAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, notFound: "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var archived = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT archived_at IS NOT NULL FROM book WHERE id = @bookId AND kind = 'book' AND owner_id = @owner FOR UPDATE",
            new { bookId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("That book doesn't exist.");
        if (!archived)
        {
            throw ApiException.Conflict("Archive the book first: only an archived book can be deleted.");
        }
        // Soft delete (#85): the row and everything under it stay; book_access, and the live-book filters, hide it.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE book SET deleted_at = now() WHERE id = @bookId",
            new { bookId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        await events.PublishAsync(new BookMetaChanged(bookId, ["deleted"]));
    }
}
