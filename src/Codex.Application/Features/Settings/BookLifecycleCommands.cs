using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Jobs;
using Codex.Contracts.Settings;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// Archive & export: duplicate, archive and restore, and queueing the source export. Duplicating (a new edition too),
// archiving and restoring are the owner's (#80); the source export needs write, since an export is a full copy of the
// manuscript. Archiving deletes the book's own grants (AccessChanged tells those people). Grants on its series stay,
// so a series collaborator keeps the archived book (it is still in the series), though only the owner's archived
// shelf lists it; restoring brings no grant back.
[RegisterSingleton]
public sealed class BookLifecycleCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events, IAccessChanges changes)
{
    private const int MaxTitleLength = 300;
    private const string BookNotFound = "That book doesn't exist.";

    public Task<DuplicatedBookDto> DuplicateAsync(Guid bookId, DuplicateBookRequest request, CancellationToken cancellationToken = default) =>
        CopyAsync(bookId, request, withSources: false, cancellationToken);

    // A second edition: a duplicate that also carries the sources (sign-offs and permission requests cleared) with their uses.
    public Task<DuplicatedBookDto> NewEditionAsync(Guid bookId, DuplicateBookRequest request, CancellationToken cancellationToken = default) =>
        CopyAsync(bookId, request, withSources: true, cancellationToken);

    private async Task<DuplicatedBookDto> CopyAsync(Guid bookId, DuplicateBookRequest request, bool withSources, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, BookNotFound);
        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            throw ApiException.Validation("The copy needs a title.");
        }
        if (title.Length > MaxTitleLength)
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }

        Guid id;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await EnsureBookAsync(connection, bookId, cancellationToken);
            // duplicate_book() numbers a copy in a series max + 1; the series row lock (the one creating a book and
            // renumbering take first) makes concurrent copies take turns instead of sharing a number.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1 FROM book s JOIN book b ON b.series_id = s.id WHERE b.id = @bookId FOR NO KEY UPDATE OF s",
                new { bookId }, transaction, cancellationToken: cancellationToken));
            id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                withSources ? "SELECT new_edition(@bookId, @title)" : "SELECT duplicate_book(@bookId, @title)", new { bookId, title }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(id, ["created"]));
        return new DuplicatedBookDto(id, title);
    }

    public Task ArchiveAsync(Guid bookId, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(bookId, archive: true, cancellationToken);

    public Task RestoreAsync(Guid bookId, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(bookId, archive: false, cancellationToken);

    // The export worker builds the .zip. 409 while one for the book is queued or running.
    public async Task<JobDto> QueueSourceExportAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, BookNotFound);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureBookAsync(connection, bookId, cancellationToken);
        var running = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM export_job WHERE book_id = @bookId AND kind = 'source' AND status IN ('queued', 'running'))",
            new { bookId }, cancellationToken: cancellationToken));
        if (running)
        {
            throw ApiException.Conflict("An export is already running. Wait for it to finish.");
        }

        return await connection.QuerySingleAsync<JobDto>(new CommandDefinition(
            """
            INSERT INTO export_job (book_id, kind, created_by) VALUES (@bookId, 'source', @user)
            RETURNING id, book_id AS BookId, kind, status AS State, 0 AS Percent, step AS Message,
                      NULL::text AS ResultUrl, created_at AS CreatedAt, created_at AS UpdatedAt
            """,
            new { bookId, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    private async Task SetArchivedAsync(Guid bookId, bool archive, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, BookNotFound);
        var before = archive ? await changes.CaptureAsync(await WithSeriesAsync(bookId, cancellationToken), null, cancellationToken) : null;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (archive && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (SELECT 1 FROM export_job
                           WHERE book_id = @bookId AND kind IN ('proof', 'print') AND status IN ('queued', 'running'))
            """,
            new { bookId }, cancellationToken: cancellationToken)))
        {
            await EnsureBookAsync(connection, bookId, cancellationToken);
            throw ApiException.Blocked("An export is running for this book. Wait for it to finish, then archive.");
        }
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE book SET archived_at = CASE WHEN @archive THEN now() END
                WHERE id = @bookId AND kind = 'book' AND (archived_at IS NULL) = @archive
                """,
                new { bookId, archive }, transaction, cancellationToken: cancellationToken));
            if (changed == 0)
            {
                await EnsureBookAsync(connection, bookId, cancellationToken);
                throw ApiException.Conflict(archive ? "That book is already archived." : "That book isn't archived.");
            }
            if (archive)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE book_grant SET archived_at = now(), updated_at = now() WHERE book_id = @bookId AND archived_at IS NULL", new { bookId }, transaction, cancellationToken: cancellationToken));
            }
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new BookMetaChanged(bookId, ["archived"]));
        if (before is not null)
        {
            await changes.PublishAsync(before, cancellationToken);
        }
    }

    // The book and its series row: a grant on the book also gives view on the series' shared items.
    private async Task<Guid[]> WithSeriesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM book WHERE id = @bookId UNION SELECT series_id FROM book WHERE id = @bookId AND series_id IS NOT NULL",
            new { bookId }, cancellationToken: cancellationToken))).ToArray();
    }

    // A series id passes the access check; it is not a book.
    private static async Task EnsureBookAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken)
    {
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')",
            new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound(BookNotFound);
        }
    }
}
