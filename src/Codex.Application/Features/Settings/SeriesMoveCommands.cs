using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Codex.Contracts.Sharing;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// Moving a book to another series (or out of one). The triggers book_copy_down_series_scope (leaving a series) and
// book_join_series (joining one) do the copy-down; this command sets series_id and series_number in one UPDATE,
// then closes the gap in the old series. Locks: series rows by id (FOR NO KEY UPDATE), then every involved book
// by id (FOR UPDATE) - the order the Library lane uses.
// Sharing (#80): moving is the owner's, between the owner's own series (the owner_id filters stay on purpose). A
// series' grants reach every book in it, so the move changes who inherits access: the preview counts the people the
// target series is shared with and those who see the book only through its current series, and the move raises
// AccessChanged for everyone whose role on the book (or on either series row, through a grant on the book) changed.
[RegisterSingleton]
public sealed class SeriesMoveCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events, IAccessChanges changes)
{
    private const string BookNotFound = "That book doesn't exist.";

    private sealed record Own(string Title, Guid? SeriesId, string? SeriesName, int? Number);

    private sealed record Target(Guid Id, string Name, int NextNumber);

    private sealed record Member(Guid Id, int? Number, bool Archived, Guid? SeriesId);

    private sealed record Counts(
        int Categories, int Entries, int Labels, int Links, int Merging, int Following, int TargetSharedWith, int LosingAccess);

    public async Task<SeriesMovePreviewDto> PreviewAsync(Guid bookId, SeriesMoveRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, BookNotFound);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var own = await ReadOwnAsync(connection, null, bookId, cancellationToken);
        var target = await ReadTargetAsync(connection, null, own, request.SeriesId, cancellationToken);

        var counts = await connection.QuerySingleAsync<Counts>(new CommandDefinition(
            """
            SELECT
              (SELECT count(*) FROM kb_category WHERE book_id = @old AND archived_at IS NULL)::int AS Categories,
              (SELECT count(*) FROM kb_entry WHERE book_id = @old AND archived_at IS NULL)::int AS Entries,
              (SELECT count(*) FROM label WHERE book_id = @old AND archived_at IS NULL)::int AS Labels,
              ((SELECT count(*) FROM scene_kb x JOIN scene s ON s.id = x.scene_id JOIN kb_entry e ON e.id = x.kb_entry_id
                 WHERE s.book_id = @bookId AND e.book_id = @old AND x.archived_at IS NULL)
             + (SELECT count(*) FROM beat_kb x JOIN beat b ON b.id = x.beat_id JOIN scene s ON s.id = b.scene_id
                 JOIN kb_entry e ON e.id = x.kb_entry_id WHERE s.book_id = @bookId AND e.book_id = @old AND x.archived_at IS NULL)
             + (SELECT count(*) FROM kb_progression x JOIN scene s ON s.id = x.scene_id JOIN kb_entry e ON e.id = x.kb_entry_id
                 WHERE s.book_id = @bookId AND e.book_id = @old)
             + (SELECT count(*) FROM reference r JOIN kb_entry e ON e.id = r.kb_entry_id
                 WHERE r.book_id = @bookId AND e.book_id = @old))::int AS Links,
              (SELECT count(*) FROM label bl JOIN label sl ON sl.name = bl.name
                 WHERE bl.book_id = @bookId AND sl.book_id = @target AND bl.archived_at IS NULL AND sl.archived_at IS NULL)::int AS Merging,
              (SELECT count(*) FROM book WHERE series_id = @old AND id <> @bookId AND archived_at IS NULL
                 AND series_number > @number)::int AS Following,
              (SELECT count(*) FROM book_grant WHERE book_id = @target AND archived_at IS NULL)::int AS TargetSharedWith,
              (SELECT count(*) FROM book_access a
                 WHERE @old::uuid IS NOT NULL AND a.book_id = @bookId AND a.role <> 'owner'
                   AND NOT EXISTS (SELECT 1 FROM book_grant g WHERE g.user_id = a.user_id AND g.book_id = @bookId AND g.archived_at IS NULL)
                   AND NOT EXISTS (SELECT 1 FROM book_grant g WHERE g.user_id = a.user_id AND g.book_id = @target AND g.archived_at IS NULL))::int AS LosingAccess
            """,
            new { bookId, old = own.SeriesId, target = target?.Id, number = own.Number },
            cancellationToken: cancellationToken));

        var steps = new List<SeriesMoveStepDto>();
        var notes = new List<string>();
        var warnings = new List<string>();
        if (own.SeriesId != null)
        {
            steps.Add(new("copyCategories", $"Copy {Plural(counts.Categories, "category", "categories")} from {own.SeriesName} onto this book."));
            steps.Add(new("copyEntries", $"Copy {Plural(counts.Entries, "entry", "entries")} from {own.SeriesName} onto this book."));
            steps.Add(new("copyLabels", $"Copy {Plural(counts.Labels, "label", "labels")} from {own.SeriesName} onto this book."));
            steps.Add(new("repoint", $"Repoint {Plural(counts.Links, "link", "links")} from scenes, beats and References to the copies, and rewrite mentions in the text."));
            steps.Add(new("keepSettings", $"Keep the publication settings this book inherited from {own.SeriesName}."));
            notes.Add($"{own.SeriesName} and its other books don't change.");
            warnings.Add("Moving the book back won't merge the copies again.");
        }
        if (target != null)
        {
            steps.Add(new("join", $"Join {target.Name} as {BookNumber(target.NextNumber)}. Same-named categories and labels merge ({Plural(counts.Merging, "label", "labels")} here)."));
        }
        else
        {
            steps.Add(new("join", "Leave the book standalone: it has no series number."));
        }
        if (own.SeriesId != null && counts.Following > 0)
        {
            steps.Add(new("renumber", $"Renumber the {Plural(counts.Following, "later book", "later books")} in {own.SeriesName} to close the gap."));
        }

        return new SeriesMovePreviewDto(
            bookId, own.Title,
            new SeriesMoveEndDto(own.SeriesId, own.SeriesName, own.Number),
            new SeriesMoveEndDto(target?.Id, target?.Name, target?.NextNumber),
            steps, notes, warnings, counts.TargetSharedWith, counts.LosingAccess);
    }

    public async Task MoveAsync(Guid bookId, SeriesMoveRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, BookNotFound);
        var before = await changes.CaptureAsync(await InvolvedAsync(bookId, request.SeriesId, cancellationToken), null, cancellationToken);
        List<(Guid Id, int Number)> renumbered;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var own = await ReadOwnAsync(connection, transaction, bookId, cancellationToken);
            var target = await ReadTargetAsync(connection, transaction, own, request.SeriesId, cancellationToken);
            var seriesIds = new[] { own.SeriesId, target?.Id }.OfType<Guid>().Distinct().ToArray();

            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1 FROM book WHERE id = ANY(@seriesIds) AND kind = 'series' ORDER BY id FOR NO KEY UPDATE",
                new { seriesIds }, transaction, cancellationToken: cancellationToken));
            var members = (await connection.QueryAsync<Member>(new CommandDefinition(
                """
                SELECT id, series_number AS Number, (archived_at IS NOT NULL) AS Archived, series_id AS SeriesId
                FROM book WHERE series_id = ANY(@seriesIds) OR id = @bookId ORDER BY id FOR UPDATE
                """,
                new { seriesIds, bookId }, transaction, cancellationToken: cancellationToken))).ToList();
            if (members.Single(m => m.Id == bookId).SeriesId != own.SeriesId)
            {
                throw ApiException.Conflict("This book's series changed. Reload and try again.");
            }

            var number = target == null
                ? (int?)null
                : members.Where(m => m.SeriesId == target.Id).Select(m => m.Number ?? 0).DefaultIfEmpty(0).Max() + 1;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET series_id = @target, series_number = @number WHERE id = @bookId",
                new { target = target?.Id, number, bookId }, transaction, cancellationToken: cancellationToken));

            var rest = own.SeriesId == null
                ? []
                : members.Where(m => m.SeriesId == own.SeriesId && m.Id != bookId)
                    .OrderBy(m => m.Archived).ThenBy(m => m.Number).ToList();
            renumbered = rest.Select((m, index) => (m.Id, Number: index + 1)).Where(x => members.First(m => m.Id == x.Id).Number != x.Number).ToList();
            if (rest.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE book SET series_number = o.number
                    FROM unnest(@ids) WITH ORDINALITY AS o(id, number)
                    WHERE book.id = o.id AND book.series_number IS DISTINCT FROM o.number
                    """,
                    new { ids = rest.Select(m => m.Id).ToArray() }, transaction, cancellationToken: cancellationToken));
            }
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(bookId, ["seriesId", "seriesName", "seriesNumber"]));
        foreach (var (id, _) in renumbered)
        {
            await events.PublishAsync(new BookMetaChanged(id, ["seriesNumber"]));
        }
        await changes.PublishAsync(before, cancellationToken);
    }

    // The book, the series it leaves and the one it joins.
    private async Task<Guid[]> InvolvedAsync(Guid bookId, Guid? target, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var current = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT series_id FROM book WHERE id = @bookId", new { bookId }, cancellationToken: cancellationToken));
        return new[] { bookId, current, target }.OfType<Guid>().Distinct().ToArray();
    }

    private async Task<Own> ReadOwnAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid bookId, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<Own>(new CommandDefinition(
            """
            SELECT b.title, b.series_id AS SeriesId, s.title AS SeriesName, b.series_number AS Number
            FROM book b LEFT JOIN book s ON s.id = b.series_id
            WHERE b.id = @bookId AND b.kind = 'book' AND b.owner_id = @owner
            """,
            new { bookId, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound(BookNotFound);
    }

    private async Task<Target?> ReadTargetAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Own own, Guid? seriesId, CancellationToken cancellationToken)
    {
        if (seriesId == own.SeriesId)
        {
            throw ApiException.Validation("The book is already there.");
        }
        if (seriesId is not { } id)
        {
            return null;
        }
        return await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
            """
            SELECT s.id, s.title AS Name,
                   (coalesce((SELECT max(x.series_number) FROM book x WHERE x.series_id = s.id), 0) + 1)::int AS NextNumber
            FROM book s WHERE s.id = @id AND s.kind = 'series' AND s.owner_id = @owner AND s.archived_at IS NULL
            """,
            new { id, owner = currentUser.Id }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.Validation("That isn't a series.");
    }

    private static string BookNumber(int number) => $"Book {number:000}";

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}
