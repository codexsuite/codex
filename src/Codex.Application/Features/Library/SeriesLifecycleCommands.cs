using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Library;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Library;

// Making a series from standalone books and deleting one. Creating numbers the picked books 1..n in the order given
// (book_join_series merges their labels into the series'); deleting detaches every book first - book_copy_down_series_scope
// copies the series' Knowledge base, labels and assets onto each - and then archives the now empty series row (a soft
// delete: the app never hard-deletes). Both steps are one transaction. Both are the owner's (#80): a new series is made
// from the caller's own books (owner_id filter kept on purpose). Deleting a series drops its grants (as archiving a
// book drops the book's), so whoever saw its books through the series loses them unless they hold a grant on the book;
// AccessChanged tells them.
[RegisterSingleton]
public sealed class SeriesLifecycleCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events, IAccessChanges changes)
{
    private const int MaxNameLength = 200;

    public async Task<CreatedSeriesDto> CreateAsync(CreateSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw ApiException.Validation("A series needs a name.");
        }
        if (name.Length > MaxNameLength)
        {
            throw ApiException.Validation($"Name can be at most {MaxNameLength} characters.");
        }
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var authorName = string.IsNullOrWhiteSpace(request.AuthorName) ? null : request.AuthorName.Trim();
        var bookIds = request.BookIds?.ToArray() ?? [];
        if (bookIds.Distinct().Count() != bookIds.Length)
        {
            throw ApiException.Validation("Each book can be picked once.");
        }
        foreach (var id in bookIds)
        {
            await access.RequireAsync(id, BookRole.Owner, cancellationToken, notFound: "That book doesn't exist.");
        }

        Guid seriesId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var standalone = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT id FROM book
                WHERE id = ANY(@bookIds) AND kind = 'book' AND owner_id = @owner AND series_id IS NULL AND archived_at IS NULL
                ORDER BY id FOR UPDATE
                """,
                new { bookIds, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))).ToHashSet();
            if (!standalone.SetEquals(bookIds))
            {
                throw ApiException.Validation("Pick standalone books that are not archived.");
            }

            seriesId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO book (kind, owner_id, title, description, author_name) VALUES ('series', @owner, @name, @description, @authorName) RETURNING id",
                new { owner = currentUser.Id, name, description, authorName }, transaction, cancellationToken: cancellationToken));
            if (bookIds.Length > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE book SET series_id = @seriesId, series_number = o.number
                    FROM unnest(@bookIds) WITH ORDINALITY AS o(id, number)
                    WHERE book.id = o.id
                    """,
                    new { seriesId, bookIds }, transaction, cancellationToken: cancellationToken));
            }
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(seriesId, ["created"]));
        foreach (var id in bookIds)
        {
            await events.PublishAsync(new BookMetaChanged(id, ["seriesId", "seriesName", "seriesNumber"]));
        }
        return new CreatedSeriesDto(seriesId, name, bookIds);
    }

    public async Task<SeriesDeleteImpactDto> ImpactAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var name = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT title FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner",
            new { seriesId, owner = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Series not found.");
        var books = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM book WHERE series_id = @seriesId AND deleted_at IS NULL", new { seriesId }, cancellationToken: cancellationToken));
        var categories = (await connection.QueryAsync<SeriesDeleteCategoryDto>(new CommandDefinition(
            """
            SELECT c.name, count(e.id)::int AS Entries
            FROM kb_category c LEFT JOIN kb_entry e ON e.category_id = c.id AND e.archived_at IS NULL
            WHERE c.book_id = @seriesId AND c.archived_at IS NULL GROUP BY c.id, c.name, c.position ORDER BY c.position, c.name
            """,
            new { seriesId }, cancellationToken: cancellationToken))).ToList();
        var labels = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM label WHERE book_id = @seriesId AND archived_at IS NULL ORDER BY position, name",
            new { seriesId }, cancellationToken: cancellationToken))).ToList();
        return new SeriesDeleteImpactDto(seriesId, name, books, categories, labels);
    }

    public async Task DeleteAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        var before = await changes.CaptureAsync(await MembersAsync(seriesId, cancellationToken), null, cancellationToken);
        Guid[] bookIds;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // Same lock order as the other series commands: the series row, then its books by id.
            var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT id FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner FOR NO KEY UPDATE",
                new { seriesId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Series not found.");
            bookIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT id FROM book WHERE series_id = @seriesId AND deleted_at IS NULL ORDER BY id FOR UPDATE",
                new { seriesId }, transaction, cancellationToken: cancellationToken))).ToArray();
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET series_id = NULL, series_number = NULL WHERE series_id = @seriesId AND deleted_at IS NULL",
                new { seriesId = locked }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET archived_at = coalesce(archived_at, now()) WHERE id = @seriesId", new { seriesId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book_grant SET archived_at = now(), updated_at = now() WHERE book_id = @seriesId AND archived_at IS NULL", new { seriesId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(seriesId, ["deleted"]));
        foreach (var id in bookIds)
        {
            await events.PublishAsync(new BookMetaChanged(id, ["seriesId", "seriesName", "seriesNumber"]));
        }
        await changes.PublishAsync(before, cancellationToken);
    }

    // The series row and its books: the rows whose access the series' grants decide.
    private async Task<Guid[]> MembersAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM book WHERE id = @seriesId OR series_id = @seriesId",
            new { seriesId }, cancellationToken: cancellationToken))).ToArray();
    }
}
