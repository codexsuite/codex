using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// Moves a book to another place in its own series. Lock order matches the Library lane: the series row
// (FOR NO KEY UPDATE), then the series' books by id (FOR UPDATE). The deferred unique constraint lets the
// renumbering pass through duplicates inside the transaction. Renumbering reshuffles the whole series, so it is the
// owner's (#80); the owner_id filter stays on purpose.
[RegisterSingleton]
public sealed class SeriesNumberCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events)
{
    private sealed record Member(Guid Id, int Number, bool Archived);

    private sealed record Own(Guid? SeriesId, int? Number, bool Archived);

    public async Task<SeriesNumberChangeDto> SetAsync(Guid bookId, SetSeriesNumberRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, "That book doesn't exist.");
        Guid seriesId;
        List<(Guid Id, int Number, int Previous)> live;
        int previous;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var own = await connection.QuerySingleOrDefaultAsync<Own>(new CommandDefinition(
                """
                SELECT series_id AS SeriesId, series_number AS Number, (archived_at IS NOT NULL) AS Archived
                FROM book WHERE id = @bookId AND kind = 'book' AND owner_id = @owner
                """,
                new { bookId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("That book doesn't exist.");
            if (own.SeriesId is not { } id)
            {
                throw ApiException.Validation("This book isn't in a series.");
            }
            seriesId = id;

            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1 FROM book WHERE id = @seriesId AND kind = 'series' FOR NO KEY UPDATE",
                new { seriesId }, transaction, cancellationToken: cancellationToken));
            var members = (await connection.QueryAsync<Member>(new CommandDefinition(
                """
                SELECT id, series_number AS Number, (archived_at IS NOT NULL) AS Archived
                FROM book WHERE series_id = @seriesId ORDER BY id FOR UPDATE
                """,
                new { seriesId }, transaction, cancellationToken: cancellationToken))).ToList();
            var me = members.SingleOrDefault(m => m.Id == bookId)
                ?? throw ApiException.Conflict("This book left the series. Reload and try again.");
            if (me.Archived)
            {
                throw ApiException.Validation("An archived book keeps its number.");
            }

            var liveMembers = members.Where(m => !m.Archived).OrderBy(m => m.Number).ToList();
            if (request.Number < 1 || request.Number > liveMembers.Count)
            {
                throw ApiException.Validation($"Number must be between 1 and {liveMembers.Count}.");
            }

            previous = me.Number;
            var order = liveMembers.Where(m => m.Id != bookId).ToList();
            order.Insert(request.Number - 1, me);
            var archivedInOrder = members.Where(m => m.Archived).OrderBy(m => m.Number);
            var all = order.Concat(archivedInOrder).ToList();
            live = order.Select((m, index) => (m.Id, index + 1, m.Number)).ToList();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE book SET series_number = o.number
                FROM unnest(@ids) WITH ORDINALITY AS o(id, number)
                WHERE book.id = o.id AND book.series_number IS DISTINCT FROM o.number
                """,
                new { ids = all.Select(m => m.Id).ToArray() }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var entry in live.Where(e => e.Number != e.Previous))
        {
            await events.PublishAsync(new BookMetaChanged(entry.Id, ["seriesNumber"]));
        }
        return new SeriesNumberChangeDto(
            bookId, seriesId, request.Number, previous,
            live.Select(e => new SeriesNumberEntryDto(e.Id, e.Number, e.Previous)).ToList());
    }
}
