using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Library;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Library;

// Renaming and reordering a series are the owner's (#80): the owner_id filters stay on purpose.
[RegisterSingleton]
public sealed class SeriesCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events, IAccessChanges changes)
{
    private const int MaxNameLength = 200;

    private sealed record NumberedBook(Guid Id, int? SeriesNumber, bool Archived);

    public async Task<SeriesDto> PatchAsync(Guid seriesId, PatchSeriesRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        if (request.Name == null && request.Description == null)
        {
            throw ApiException.Validation("Nothing to change: give a name or description.");
        }
        var name = request.Name?.Trim();
        if (name is { Length: 0 })
        {
            throw ApiException.Validation("A series needs a name.");
        }
        if (name is { Length: > MaxNameLength })
        {
            throw ApiException.Validation($"Name can be at most {MaxNameLength} characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var series = await connection.QuerySingleOrDefaultAsync<SeriesDto>(new CommandDefinition(
            """
            UPDATE book SET
                title = coalesce(@name::text, title),
                description = CASE WHEN @description::text IS NULL THEN description ELSE nullif(btrim(@description::text), '') END
            WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NULL
            RETURNING id, title AS Name, description
            """,
            new { seriesId, name, description = request.Description, owner = currentUser.Id },
            cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Series not found.");

        await events.PublishAsync(new BookMetaChanged(seriesId, ["name", "description"]));
        return series;
    }

    public async Task<SeriesDto> GetAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SeriesDto>(new CommandDefinition(
            "SELECT id, title AS Name, description FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner",
            new { seriesId, owner = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Series not found.");
    }

    // Soft-deletes a series: sets its archived_at and leaves its books' rows alone (they keep series_id and series_number),
    // so RestoreAsync puts everything back. While it is archived its books read as archived (ShellQueries, search, list_books).
    // Writes into such a book are not blocked: they follow the book's own archived_at, as for any other book.
    // Archiving drops the series' own grants, as archiving a book drops the book's (#80); AccessChanged tells whoever
    // saw its books through them.
    public async Task ArchiveAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        var before = await changes.CaptureAsync(await MembersAsync(seriesId, cancellationToken), null, cancellationToken);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            _ = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT id FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NULL FOR NO KEY UPDATE",
                new { seriesId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Series not found.");
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET archived_at = now() WHERE id = @seriesId", new { seriesId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book_grant SET archived_at = now(), updated_at = now() WHERE book_id = @seriesId AND archived_at IS NULL", new { seriesId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(seriesId, ["archived"]));
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

    public async Task RestoreAsync(Guid seriesId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET archived_at = NULL WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NOT NULL",
                new { seriesId, owner = currentUser.Id }, cancellationToken: cancellationToken));
            if (changed == 0)
            {
                throw ApiException.NotFound("Series not found.");
            }
        }

        await events.PublishAsync(new BookMetaChanged(seriesId, ["archived"]));
    }

    // Numbers the live books 1..n in the given order. Archived books of the series keep their relative order
    // after them (n+1..), so the unique (series, number) pair never clashes at commit.
    public async Task<SeriesOrderDto> SetOrderAsync(Guid seriesId, SetSeriesOrderRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(seriesId, BookRole.Owner, cancellationToken, notFound: "Series not found.");
        var bookIds = request.BookIds?.ToArray() ?? throw ApiException.Validation("bookIds is required.");

        List<Guid> changed;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @seriesId AND kind = 'series' AND owner_id = @owner AND archived_at IS NULL)",
                new { seriesId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken));
            if (!exists)
            {
                throw ApiException.NotFound("Series not found.");
            }

            var books = (await connection.QueryAsync<NumberedBook>(new CommandDefinition(
                """
                SELECT id, series_number AS SeriesNumber, (archived_at IS NOT NULL) AS Archived
                FROM book WHERE series_id = @seriesId ORDER BY id FOR UPDATE
                """,
                new { seriesId }, transaction, cancellationToken: cancellationToken))).ToList();
            var live = books.Where(b => !b.Archived).Select(b => b.Id).ToHashSet();
            if (bookIds.Length != live.Count || bookIds.Distinct().Count() != bookIds.Length || !live.SetEquals(bookIds))
            {
                throw ApiException.Validation("bookIds must list every book of the series exactly once.");
            }

            var archivedInOrder = books.Where(b => b.Archived).OrderBy(b => b.SeriesNumber).Select(b => b.Id);
            var ordered = bookIds.Concat(archivedInOrder).ToArray();
            var current = books.ToDictionary(b => b.Id, b => b.SeriesNumber);
            changed = ordered.Select((id, index) => (id, number: index + 1))
                .Where(x => current[x.id] != x.number).Select(x => x.id).ToList();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE book SET series_number = o.number
                FROM unnest(@ids) WITH ORDINALITY AS o(id, number)
                WHERE book.id = o.id AND book.series_number IS DISTINCT FROM o.number
                """,
                new { ids = ordered }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var id in changed)
        {
            await events.PublishAsync(new BookMetaChanged(id, ["seriesNumber"]));
        }
        return new SeriesOrderDto(seriesId, bookIds.Select((id, index) => new SeriesOrderBookDto(id, index + 1)).ToList());
    }
}
