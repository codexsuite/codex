using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// Folds a duplicate source into the one that is kept (M10). One transaction:
//   * the duplicate's uses move to the kept source, archived ones too so their ids stay the ones the prose names. A `ref:` link
//     holds the use's id, so no prose changes; markers renumber because they are counted over the kept source's uses. A moved
//     live use gets changed_at = now(), the same as citing the source anew: a signed-off kept source reopens.
//   * the kept source's empty fields (title and kind are always set) are filled from the duplicate; a field with a value stays.
//   * the duplicate's live permission request moves too when the kept source has none, with its letters; otherwise it stays
//     on the archived duplicate. The signed-off and fair-use decisions stay each source's own.
//   * open quote flags whose best guess was the duplicate now point at the kept source.
//   * the duplicate is archived with merged_into_id set, and both sources get a History line (EventProvenance says who).
// Preview runs the same steps and rolls back, so its clearance is the one a merge would leave. Unmerge puts it all back while
// nothing it touched has changed since.
public interface ISourceMergeCommands
{
    // Unknown, archived or another user's source: NotFound. Validation when they are the same source or in different books.
    Task<SourceMergeResult> MergeAsync(Guid duplicateId, Guid keptId, bool dryRun, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Null when the merge can be undone: the problem otherwise (something it touched has changed since).
    Task<string?> UnmergeProblemAsync(SourceMergeSnapshot snapshot, CancellationToken cancellationToken);

    Task UnmergeAsync(SourceMergeSnapshot snapshot, CancellationToken cancellationToken, EventProvenance? provenance = null);
}

[RegisterSingleton]
public sealed class SourceMergeCommands(NpgsqlDataSource dataSource, ISourceDetailQueries queries, ICurrentUser currentUser, IBookAccess access) : ISourceMergeCommands
{
    // The columns a merge can fill, as JSON. Keys are the column names.
    private const string ColumnsSql =
        """
        SELECT jsonb_build_object('authors', authors, 'year', year, 'publisher', publisher, 'container_title', container_title,
            'volume', volume, 'issue', issue, 'page_range', page_range, 'edition', edition, 'isbn', isbn, 'doi', doi, 'url', url,
            'accessed_on', accessed_on, 'rights_holder', rights_holder, 'permissions_contact', permissions_contact,
            'kb_entry_id', kb_entry_id, 'licence', licence)::text
        FROM reference WHERE id = @id
        """;

    private sealed record SourceRow(Guid Id, Guid BookId, string Title, bool Archived);

    private sealed record Moved(Guid Id, bool Live, DateTimeOffset OldChangedAt, DateTimeOffset NewChangedAt);

    private sealed record UseState(Guid Id, DateTimeOffset ChangedAt, bool Live);

    public async Task<SourceMergeResult> MergeAsync(
        Guid duplicateId, Guid keptId, bool dryRun, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.Source, duplicateId, BookRole.Write, cancellationToken, $"Source {duplicateId} was not found.");
        await access.RequireAsync(BookItem.Source, keptId, BookRole.Write, cancellationToken, $"Source {keptId} was not found.");
        if (duplicateId == keptId)
        {
            throw ApiException.Validation("A source can't be merged into itself.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<SourceRow>(new CommandDefinition(
            "SELECT id, book_id AS BookId, title, archived_at IS NOT NULL AS Archived FROM reference WHERE id = ANY(@ids) ORDER BY id FOR UPDATE",
            new { ids = new[] { duplicateId, keptId } }, transaction, cancellationToken: cancellationToken))).ToDictionary(r => r.Id);
        foreach (var id in new[] { duplicateId, keptId })
        {
            if (!rows.TryGetValue(id, out var row) || row.Archived)
            {
                throw ApiException.NotFound($"Source {id} was not found.");
            }
        }

        var duplicate = rows[duplicateId];
        var kept = rows[keptId];
        if (duplicate.BookId != kept.BookId)
        {
            throw ApiException.Validation("Both sources must be in the same book to merge them.");
        }

        var before = await connection.ExecuteScalarAsync<string>(new CommandDefinition(ColumnsSql, new { id = keptId }, transaction, cancellationToken: cancellationToken));
        var moved = (await connection.QueryAsync<Moved>(new CommandDefinition(
            """
            WITH old AS (SELECT id, changed_at, archived_at IS NULL AS live FROM reference_use WHERE reference_id = @duplicateId FOR UPDATE)
            UPDATE reference_use u
            SET reference_id = @keptId, changed_at = CASE WHEN old.live THEN now() ELSE u.changed_at END
            FROM old
            WHERE u.id = old.id
            RETURNING u.id AS Id, old.live AS Live, old.changed_at AS OldChangedAt, u.changed_at AS NewChangedAt
            """,
            new { duplicateId, keptId }, transaction, cancellationToken: cancellationToken))).ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reference k SET
                authors = CASE WHEN jsonb_array_length(k.authors) = 0 THEN d.authors ELSE k.authors END,
                year = coalesce(k.year, d.year),
                publisher = CASE WHEN nullif(btrim(k.publisher), '') IS NULL AND nullif(btrim(d.publisher), '') IS NOT NULL THEN d.publisher ELSE k.publisher END,
                container_title = CASE WHEN nullif(btrim(k.container_title), '') IS NULL AND nullif(btrim(d.container_title), '') IS NOT NULL THEN d.container_title ELSE k.container_title END,
                volume = CASE WHEN nullif(btrim(k.volume), '') IS NULL AND nullif(btrim(d.volume), '') IS NOT NULL THEN d.volume ELSE k.volume END,
                issue = CASE WHEN nullif(btrim(k.issue), '') IS NULL AND nullif(btrim(d.issue), '') IS NOT NULL THEN d.issue ELSE k.issue END,
                page_range = CASE WHEN nullif(btrim(k.page_range), '') IS NULL AND nullif(btrim(d.page_range), '') IS NOT NULL THEN d.page_range ELSE k.page_range END,
                edition = CASE WHEN nullif(btrim(k.edition), '') IS NULL AND nullif(btrim(d.edition), '') IS NOT NULL THEN d.edition ELSE k.edition END,
                isbn = CASE WHEN nullif(btrim(k.isbn), '') IS NULL AND nullif(btrim(d.isbn), '') IS NOT NULL THEN d.isbn ELSE k.isbn END,
                doi = CASE WHEN nullif(btrim(k.doi), '') IS NULL AND nullif(btrim(d.doi), '') IS NOT NULL THEN d.doi ELSE k.doi END,
                url = CASE WHEN nullif(btrim(k.url), '') IS NULL AND nullif(btrim(d.url), '') IS NOT NULL THEN d.url ELSE k.url END,
                accessed_on = coalesce(k.accessed_on, d.accessed_on),
                rights_holder = CASE WHEN nullif(btrim(k.rights_holder), '') IS NULL AND nullif(btrim(d.rights_holder), '') IS NOT NULL THEN d.rights_holder ELSE k.rights_holder END,
                permissions_contact = CASE WHEN nullif(btrim(k.permissions_contact), '') IS NULL AND nullif(btrim(d.permissions_contact), '') IS NOT NULL THEN d.permissions_contact ELSE k.permissions_contact END,
                kb_entry_id = coalesce(k.kb_entry_id, d.kb_entry_id),
                licence = CASE WHEN k.licence = 'unknown' THEN d.licence ELSE k.licence END
            FROM reference d
            WHERE k.id = @keptId AND d.id = @duplicateId
            """,
            new { duplicateId, keptId }, transaction, cancellationToken: cancellationToken));
        var after = await connection.ExecuteScalarAsync<string>(new CommandDefinition(ColumnsSql, new { id = keptId }, transaction, cancellationToken: cancellationToken));
        var filled = ChangedKeys(before!, after!);

        // The duplicate's live request follows when the kept source has none (at most one live request per source).
        var permission = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)?>(new CommandDefinition(
            """
            SELECT p.id, p.status FROM permission_request p
            WHERE p.reference_id = @duplicateId AND p.status IN ('draft', 'sent', 'granted')
              AND NOT EXISTS (SELECT 1 FROM permission_request q WHERE q.reference_id = @keptId AND q.status IN ('draft', 'sent', 'granted'))
            """,
            new { duplicateId, keptId }, transaction, cancellationToken: cancellationToken));
        var attachments = new List<Guid>();
        if (permission is { } request)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE permission_request SET reference_id = @keptId WHERE id = @id", new { keptId, id = request.Id }, transaction, cancellationToken: cancellationToken));
            attachments.AddRange(await connection.QueryAsync<Guid>(new CommandDefinition(
                "UPDATE reference_attachment SET reference_id = @keptId WHERE permission_request_id = @id RETURNING id",
                new { keptId, id = request.Id }, transaction, cancellationToken: cancellationToken)));
        }

        var flags = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "UPDATE quote_flag SET reference_id = @keptId WHERE reference_id = @duplicateId AND status = 'open' RETURNING id",
            new { duplicateId, keptId }, transaction, cancellationToken: cancellationToken))).ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET archived_at = now(), merged_into_id = @keptId WHERE id = @duplicateId", new { duplicateId, keptId }, transaction, cancellationToken: cancellationToken));
        var live = moved.Count(m => m.Live);
        await SourceEvents.RecordAsync(connection, transaction, keptId, currentUser.Id, SourceEvents.MergedIn,
            new { from = duplicate.Title, uses = live, fields = string.Join(", ", filled) }, cancellationToken, provenance);
        await SourceEvents.RecordAsync(connection, transaction, duplicateId, currentUser.Id, SourceEvents.MergedInto, new { into = kept.Title }, cancellationToken, provenance);

        var result = new SourceMergeResult(duplicate.Title, kept.Title, live, moved.Count - live, filled, permission != null, flags.Count,
            new SourceMergeSnapshot(duplicateId, keptId, moved.Select(m => new SourceMergeSnapshot.MovedUse(m.Id, m.Live, m.OldChangedAt, m.NewChangedAt)).ToList(),
                before!, after!, filled, permission?.Id, permission?.Status, attachments, flags),
            await queries.GetAsync(connection, keptId, cancellationToken));
        if (dryRun)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<string?> UnmergeProblemAsync(SourceMergeSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var kept = await connection.QuerySingleOrDefaultAsync<(bool Archived, Guid BookId)?>(new CommandDefinition(
            "SELECT archived_at IS NOT NULL, book_id FROM reference WHERE id = @id", new { id = snapshot.KeptId }, cancellationToken: cancellationToken));
        var duplicate = await connection.QuerySingleOrDefaultAsync<(bool Archived, Guid? MergedInto)?>(new CommandDefinition(
            "SELECT archived_at IS NOT NULL, merged_into_id FROM reference WHERE id = @id", new { id = snapshot.DuplicateId }, cancellationToken: cancellationToken));
        if (kept is not { Archived: false } || duplicate is not { Archived: true } || duplicate.Value.MergedInto != snapshot.KeptId)
        {
            return "A source this merge touched was archived, restored or merged again.";
        }

        var uses = (await connection.QueryAsync<UseState>(new CommandDefinition(
            "SELECT id AS Id, changed_at AS ChangedAt, archived_at IS NULL AS Live FROM reference_use WHERE reference_id = @keptId AND id = ANY(@ids)",
            new { snapshot.KeptId, ids = snapshot.Uses.Select(u => u.Id).ToArray() }, cancellationToken: cancellationToken))).ToDictionary(u => u.Id);
        if (snapshot.Uses.Any(u => !uses.TryGetValue(u.Id, out var now) || now.Live != u.Live || now.ChangedAt != u.NewChangedAt))
        {
            return "A use this merge moved was retyped, lengthened, removed or moved again.";
        }

        var current = await connection.ExecuteScalarAsync<string>(new CommandDefinition(ColumnsSql, new { id = snapshot.KeptId }, cancellationToken: cancellationToken));
        using var wanted = JsonDocument.Parse(snapshot.After);
        using var now2 = JsonDocument.Parse(current!);
        if (snapshot.Filled.Any(k => wanted.RootElement.GetProperty(k).GetRawText() != now2.RootElement.GetProperty(k).GetRawText()))
        {
            return "A field this merge filled was edited since.";
        }

        if (snapshot.PermissionId is { } permissionId
            && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT NOT EXISTS (SELECT 1 FROM permission_request WHERE id = @permissionId AND reference_id = @keptId AND status = @status)",
                new { permissionId, snapshot.KeptId, status = snapshot.PermissionStatus }, cancellationToken: cancellationToken)))
        {
            return "The permission request this merge moved was sent, answered, withdrawn or moved again.";
        }

        return null;
    }

    public async Task UnmergeAsync(SourceMergeSnapshot snapshot, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.Source, snapshot.KeptId, BookRole.Write, cancellationToken, $"Source {snapshot.KeptId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM reference WHERE id = ANY(@ids) ORDER BY id FOR UPDATE", new { ids = new[] { snapshot.DuplicateId, snapshot.KeptId } }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET archived_at = NULL, merged_into_id = NULL WHERE id = @id", new { id = snapshot.DuplicateId }, transaction, cancellationToken: cancellationToken));
        foreach (var use in snapshot.Uses)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE reference_use SET reference_id = @duplicateId, changed_at = @old WHERE id = @id",
                new { snapshot.DuplicateId, id = use.Id, old = use.OldChangedAt }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reference SET
                authors = CASE WHEN 'authors' = ANY(@filled) THEN (@before::jsonb) -> 'authors' ELSE authors END,
                year = CASE WHEN 'year' = ANY(@filled) THEN ((@before::jsonb) ->> 'year')::integer ELSE year END,
                publisher = CASE WHEN 'publisher' = ANY(@filled) THEN (@before::jsonb) ->> 'publisher' ELSE publisher END,
                container_title = CASE WHEN 'container_title' = ANY(@filled) THEN (@before::jsonb) ->> 'container_title' ELSE container_title END,
                volume = CASE WHEN 'volume' = ANY(@filled) THEN (@before::jsonb) ->> 'volume' ELSE volume END,
                issue = CASE WHEN 'issue' = ANY(@filled) THEN (@before::jsonb) ->> 'issue' ELSE issue END,
                page_range = CASE WHEN 'page_range' = ANY(@filled) THEN (@before::jsonb) ->> 'page_range' ELSE page_range END,
                edition = CASE WHEN 'edition' = ANY(@filled) THEN (@before::jsonb) ->> 'edition' ELSE edition END,
                isbn = CASE WHEN 'isbn' = ANY(@filled) THEN (@before::jsonb) ->> 'isbn' ELSE isbn END,
                doi = CASE WHEN 'doi' = ANY(@filled) THEN (@before::jsonb) ->> 'doi' ELSE doi END,
                url = CASE WHEN 'url' = ANY(@filled) THEN (@before::jsonb) ->> 'url' ELSE url END,
                accessed_on = CASE WHEN 'accessed_on' = ANY(@filled) THEN ((@before::jsonb) ->> 'accessed_on')::date ELSE accessed_on END,
                rights_holder = CASE WHEN 'rights_holder' = ANY(@filled) THEN (@before::jsonb) ->> 'rights_holder' ELSE rights_holder END,
                permissions_contact = CASE WHEN 'permissions_contact' = ANY(@filled) THEN (@before::jsonb) ->> 'permissions_contact' ELSE permissions_contact END,
                kb_entry_id = CASE WHEN 'kb_entry_id' = ANY(@filled) THEN ((@before::jsonb) ->> 'kb_entry_id')::uuid ELSE kb_entry_id END,
                licence = CASE WHEN 'licence' = ANY(@filled) THEN (@before::jsonb) ->> 'licence' ELSE licence END
            WHERE id = @keptId
            """,
            new { snapshot.KeptId, snapshot.Before, filled = snapshot.Filled.ToArray() }, transaction, cancellationToken: cancellationToken));
        if (snapshot.PermissionId is { } permissionId)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE permission_request SET reference_id = @duplicateId WHERE id = @permissionId", new { snapshot.DuplicateId, permissionId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE reference_attachment SET reference_id = @duplicateId WHERE id = ANY(@ids)", new { snapshot.DuplicateId, ids = snapshot.Attachments.ToArray() }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE quote_flag SET reference_id = @duplicateId WHERE id = ANY(@ids) AND reference_id = @keptId", new { snapshot.DuplicateId, snapshot.KeptId, ids = snapshot.Flags.ToArray() }, transaction, cancellationToken: cancellationToken));
        var titles = (await connection.QueryAsync<(Guid Id, string Title)>(new CommandDefinition(
            "SELECT id, title FROM reference WHERE id = ANY(@ids)", new { ids = new[] { snapshot.DuplicateId, snapshot.KeptId } }, transaction, cancellationToken: cancellationToken))).ToDictionary(t => t.Id, t => t.Title);
        await SourceEvents.RecordAsync(connection, transaction, snapshot.KeptId, currentUser.Id, SourceEvents.MergeUndone, new { other = titles[snapshot.DuplicateId] }, cancellationToken, provenance);
        await SourceEvents.RecordAsync(connection, transaction, snapshot.DuplicateId, currentUser.Id, SourceEvents.MergeUndone, new { other = titles[snapshot.KeptId] }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
    }

    // The keys whose value differs between two JSON objects.
    private static List<string> ChangedKeys(string before, string after)
    {
        using var a = JsonDocument.Parse(before);
        using var b = JsonDocument.Parse(after);
        return b.RootElement.EnumerateObject().Where(p => a.RootElement.GetProperty(p.Name).GetRawText() != p.Value.GetRawText()).Select(p => p.Name).ToList();
    }
}
