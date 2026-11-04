using System.Globalization;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

[RegisterSingleton]
public sealed class SourceCommands(
    NpgsqlDataSource dataSource,
    ISourceDetailQueries queries,
    ISourceLookup lookup,
    IProseStore proseStore,
    ICurrentUser currentUser,
    TimeProvider clock,
    IBookAccess access) : ISourceCommands
{
    private static readonly HashSet<string> CitedFields = ["kind", "title", "authors", "year", "publisher", "edition", "ISBN", "DOI", "URL"];

    private static readonly string[] UseKinds = ["quote", "paraphrase", "data", "figure", "lyric"];

    public async Task<SourceDetailDto> SignOffAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await StatusAsync(connection, transaction, sourceId, cancellationToken);
        if (status == "risk")
        {
            throw ApiException.Blocked("A copyright risk is open on this source: get permission or remove the uses before clearing it.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET signed_off_at = now(), signed_off_by = @user WHERE id = @sourceId",
            new { sourceId, user = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        await RecordAsync(connection, transaction, sourceId, SourceEvents.SignedOff, new { }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> ReopenAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await StatusAsync(connection, transaction, sourceId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET signed_off_at = NULL, signed_off_by = NULL WHERE id = @sourceId",
            new { sourceId }, transaction, cancellationToken: cancellationToken));
        await RecordAsync(connection, transaction, sourceId, SourceEvents.Reopened, new { }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> AcceptFairUseAsync(Guid sourceId, FairUseRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw ApiException.Validation("Say why this is fair use.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await StatusAsync(connection, transaction, sourceId, cancellationToken);
        var needsDecision = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT l.needs_fair_use_decision FROM reference r JOIN licence l ON l.code = r.licence WHERE r.id = @sourceId",
            new { sourceId }, transaction, cancellationToken: cancellationToken));
        if (!needsDecision)
        {
            throw ApiException.Validation("This licence needs no fair-use decision.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET fair_use_decided_at = now(), fair_use_decided_by = @user, fair_use_note = @reason WHERE id = @sourceId",
            new { sourceId, user = currentUser.Id, reason }, transaction, cancellationToken: cancellationToken));
        await RecordAsync(connection, transaction, sourceId, SourceEvents.FairUseAccepted, new { reason }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> PatchAsync(Guid sourceId, PatchSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var before = await queries.GetAsync(connection, sourceId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.Details is { } details)
            {
                await WriteDetailsAsync(connection, transaction, sourceId, before.Details, details, cancellationToken, provenance);
            }

            if (request.Entry is { } entry && entry.EntryId != before.Entry?.Id)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE reference SET kb_entry_id = @entryId WHERE id = @sourceId",
                    new { sourceId, entryId = entry.EntryId }, transaction, cancellationToken: cancellationToken));
                var name = entry.EntryId is null ? null : await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT name FROM kb_entry WHERE id = @entryId", new { entryId = entry.EntryId }, transaction, cancellationToken: cancellationToken));
                await RecordAsync(connection, transaction, sourceId,
                    entry.EntryId is null ? SourceEvents.EntryUnlinked : SourceEvents.EntryLinked, new { name }, cancellationToken, provenance);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            throw ApiException.Validation(e.MessageText);
        }

        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> SetUseKindAsync(Guid useId, PatchUseRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.ReferenceUse, useId, BookRole.Write, cancellationToken, $"Use {useId} was not found.");
        if (!UseKinds.Contains(request.Kind, StringComparer.Ordinal))
        {
            throw ApiException.Validation($"Use type '{request.Kind}' is not one of {string.Join(", ", UseKinds)}.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<UseKindRow>(new CommandDefinition(
            """
            SELECT u.reference_id AS ReferenceId, u.kind AS Kind FROM reference_use u
            JOIN reference r ON r.id = u.reference_id
            WHERE u.id = @useId AND u.archived_at IS NULL AND r.archived_at IS NULL FOR UPDATE OF u
            """,
            new { useId }, transaction, cancellationToken: cancellationToken));
        if (current is null)
        {
            throw ApiException.NotFound($"Use {useId} was not found.");
        }

        if (current.Kind != request.Kind)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE reference_use SET kind = @kind WHERE id = @useId", new { useId, kind = request.Kind }, transaction, cancellationToken: cancellationToken));
            await RecordAsync(connection, transaction, current.ReferenceId, SourceEvents.UseRetyped, new { from = current.Kind, to = request.Kind }, cancellationToken, provenance);
        }

        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, current.ReferenceId, cancellationToken);
    }

    public async Task<RecheckResultDto> RecheckAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var details = (await queries.GetAsync(connection, sourceId, cancellationToken)).Details;
        var identifiers = new[]
        {
            (Field: SourceLookupKinds.Isbn, Value: details.Isbn),
            (Field: SourceLookupKinds.Doi, Value: details.Doi),
            (Field: SourceLookupKinds.Url, Value: details.Url),
        }.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList();

        var fields = new List<RecheckFieldDto>();
        foreach (var (field, value) in identifiers)
        {
            var result = await lookup.LookupAsync(field, value!, cancellationToken);
            fields.Add(new RecheckFieldDto(field, value!, result.Outcome switch
            {
                SourceLookupResult.FoundOutcome => "ok",
                SourceLookupResult.RedirectOutcome => "redirect",
                _ => "not_found",
            }, result.RedirectTo));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var field in fields)
        {
            await UpsertCheckAsync(connection, transaction, sourceId, field.Field, field.Value, field.Outcome, field.RedirectTo, cancellationToken);
        }

        if (fields.Count > 0)
        {
            await RecordAsync(connection, transaction, sourceId, SourceEvents.LinksChecked,
                new { fields = string.Join(", ", fields.Select(f => f.Field.ToUpperInvariant())) }, cancellationToken, new EventProvenance("link_check"));
        }

        await transaction.CommitAsync(cancellationToken);
        return new RecheckResultDto(fields, await queries.GetAsync(connection, sourceId, cancellationToken));
    }

    public async Task<SourceDetailDto> AcceptRedirectAsync(Guid sourceId, AcceptRedirectRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        var target = request.RedirectTo?.Trim() ?? string.Empty;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await StatusAsync(connection, transaction, sourceId, cancellationToken);
        var field = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT c.field FROM reference_check c JOIN reference r ON r.id = c.reference_id
            WHERE c.reference_id = @sourceId AND c.status = 'redirect' AND c.resolved_url = @target
              AND c.checked_value = to_jsonb(r) ->> c.field
            """,
            new { sourceId, target }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.Validation($"No re-check reported a redirect to '{target}'.");

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reference SET isbn = CASE WHEN @field = 'isbn' THEN @target ELSE isbn END,
                                 doi = CASE WHEN @field = 'doi' THEN @target ELSE doi END,
                                 url = CASE WHEN @field = 'url' THEN @target ELSE url END
            WHERE id = @sourceId
            """,
            new { sourceId, field, target }, transaction, cancellationToken: cancellationToken));
        await UpsertCheckAsync(connection, transaction, sourceId, field, target, "ok", null, cancellationToken);
        await RecordAsync(connection, transaction, sourceId, SourceEvents.RedirectAccepted, new { field = field.ToUpperInvariant(), to = target }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<ArchiveSourceResult> ArchiveAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var uses = await LiveUsesAsync(connection, sourceId, cancellationToken);
        var (edits, changed) = await PlanUnwrapAsync(uses.Where(u => u.GenerationId is null).ToList(), cancellationToken);

        // One IProseStore transaction: the scenes are unwrapped, then (InTransaction) the uses and the source are
        // archived and the event recorded. A failing archive step leaves every scene as it was.
        await proseStore.UpdateManyAsync(edits, "manual", new ProseWriteOptions
        {
            InTransaction = async (dbConnection, dbTransaction, ct) =>
            {
                var transaction = (NpgsqlTransaction)dbTransaction;
                var npgsql = (NpgsqlConnection)dbConnection;
                await npgsql.ExecuteAsync(new CommandDefinition(
                    "UPDATE reference_use SET archived_at = now() WHERE reference_id = @sourceId AND archived_at IS NULL",
                    new { sourceId }, transaction, cancellationToken: ct));
                await npgsql.ExecuteAsync(new CommandDefinition(
                    "UPDATE reference SET archived_at = now() WHERE id = @sourceId", new { sourceId }, transaction, cancellationToken: ct));
                await npgsql.ExecuteAsync(new CommandDefinition(
                    "UPDATE quote_flag SET reference_id = NULL WHERE reference_id = @sourceId", new { sourceId }, transaction, cancellationToken: ct));
                await RecordAsync(npgsql, transaction, sourceId, SourceEvents.Archived, new { scenes = changed.Count }, ct, provenance);
            },
        }, cancellationToken);

        var numbers = (await connection.QueryAsync<ArchivedSceneDto>(new CommandDefinition(
            """
            SELECT s.id AS SceneId, co.chapter_number::integer AS ChapterNumber, ss.scene_number::integer AS SceneNumber, 0 AS Uses
            FROM scene s
            JOIN chapter_outline co ON co.id = s.chapter_id
            JOIN scene_stats ss ON ss.scene_id = s.id
            WHERE s.id = ANY (@ids)
            ORDER BY co.reading_order, ss.scene_number
            """,
            new { ids = changed.Keys.ToArray() }, cancellationToken: cancellationToken))).ToList();
        return new ArchiveSourceResult(numbers.Select(n => n with { Uses = changed[n.SceneId] }).ToList());
    }

    public async Task<SourceDetailDto> MergeAsync(Guid sourceId, MergeSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        var intoId = request.IntoId;
        if (intoId == sourceId)
        {
            throw ApiException.Validation("A source cannot be merged into itself.");
        }

        await RequireSourceAsync(intoId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var pair = (await connection.QueryAsync<MergeRow>(new CommandDefinition(
            """
            SELECT id AS Id, book_id AS BookId, title AS Title,
                   EXISTS (SELECT 1 FROM permission_request p WHERE p.reference_id = reference.id AND p.status IN ('draft', 'sent', 'granted')) AS LivePermission
            FROM reference WHERE id = ANY (@ids) AND archived_at IS NULL ORDER BY id FOR UPDATE
            """,
            new { ids = new[] { sourceId, intoId } }, transaction, cancellationToken: cancellationToken))).ToList();
        var duplicate = pair.SingleOrDefault(r => r.Id == sourceId) ?? throw ApiException.NotFound($"Source {sourceId} was not found.");
        var target = pair.SingleOrDefault(r => r.Id == intoId) ?? throw ApiException.NotFound($"Source {intoId} was not found.");
        if (duplicate.BookId != target.BookId)
        {
            throw ApiException.Validation("Only sources of the same book can be merged.");
        }

        if (duplicate.LivePermission && target.LivePermission)
        {
            throw ApiException.Blocked("Both have a permission request. Withdraw one first.");
        }

        // The target keeps its own details and takes the ones it lacks. The duplicate gives up its cite key first (unique per book).
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reference SET
                year = coalesce(t.year, d.year), publisher = coalesce(t.publisher, d.publisher),
                container_title = coalesce(t.container_title, d.container_title), volume = coalesce(t.volume, d.volume),
                issue = coalesce(t.issue, d.issue), page_range = coalesce(t.page_range, d.page_range),
                edition = coalesce(t.edition, d.edition), isbn = coalesce(t.isbn, d.isbn), doi = coalesce(t.doi, d.doi),
                url = coalesce(t.url, d.url), accessed_on = coalesce(t.accessed_on, d.accessed_on),
                rights_holder = coalesce(t.rights_holder, d.rights_holder),
                permissions_contact = coalesce(t.permissions_contact, d.permissions_contact),
                kb_entry_id = coalesce(t.kb_entry_id, d.kb_entry_id),
                authors = CASE WHEN t.authors = '[]'::jsonb THEN d.authors ELSE t.authors END,
                licence = CASE WHEN t.licence = 'unknown' THEN d.licence ELSE t.licence END
            FROM reference t, reference d
            WHERE reference.id = t.id AND t.id = @intoId AND d.id = @sourceId
            """,
            new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        var moved = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM reference_use WHERE reference_id = @sourceId AND archived_at IS NULL", new { sourceId }, transaction, cancellationToken: cancellationToken));
        // Moved uses are new to the target, so a sign-off it holds is reopened.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference_use SET reference_id = @intoId, changed_at = CASE WHEN archived_at IS NULL THEN now() ELSE changed_at END WHERE reference_id = @sourceId",
            new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE permission_request SET reference_id = @intoId WHERE reference_id = @sourceId", new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference_attachment SET reference_id = @intoId WHERE reference_id = @sourceId", new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE quote_flag SET reference_id = @intoId WHERE reference_id = @sourceId", new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET archived_at = now(), merged_into_id = @intoId, cite_key = NULL WHERE id = @sourceId", new { sourceId, intoId }, transaction, cancellationToken: cancellationToken));
        await RecordAsync(connection, transaction, sourceId, SourceEvents.MergedInto, new { into = target.Title }, cancellationToken, provenance);
        await RecordAsync(connection, transaction, intoId, SourceEvents.Merged, new { title = duplicate.Title, uses = moved }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
        return await queries.GetAsync(connection, intoId, cancellationToken);
    }

    private static async Task<List<UseRow>> LiveUsesAsync(NpgsqlConnection connection, Guid sourceId, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await StatusAsync(connection, transaction, sourceId, cancellationToken);
        return (await connection.QueryAsync<UseRow>(new CommandDefinition(
            "SELECT id AS Id, scene_id AS SceneId, generation_id AS GenerationId FROM reference_use WHERE reference_id = @sourceId AND archived_at IS NULL",
            new { sourceId }, transaction, cancellationToken: cancellationToken))).ToList();
    }

    // The edits that unwrap each use in its scene's prose, and scene id -> uses unwrapped for the scenes whose prose
    // changes. A use with no link in the body (already gone) is skipped.
    private async Task<(List<(Guid sceneId, int expectedVersion, Func<string, string> edit)> Edits, Dictionary<Guid, int> Changed)> PlanUnwrapAsync(
        List<UseRow> uses, CancellationToken cancellationToken)
    {
        var changed = new Dictionary<Guid, int>();
        var edits = new List<(Guid sceneId, int expectedVersion, Func<string, string> edit)>();
        foreach (var scene in uses.GroupBy(u => u.SceneId))
        {
            var ids = scene.Select(u => u.Id).ToList();
            var prose = await proseStore.GetAsync(scene.Key, cancellationToken);
            var count = Unwrap(prose.Body, ids, out _);
            if (count > 0)
            {
                changed[scene.Key] = count;
                edits.Add((scene.Key, prose.Version, body =>
                {
                    Unwrap(body, ids, out var result);
                    return result;
                }));
            }
        }

        return (edits, changed);
    }

    private static int Unwrap(string body, IEnumerable<Guid> useIds, out string result)
    {
        var document = ProseDocument.Parse(body);
        var count = 0;
        foreach (var id in useIds)
        {
            try
            {
                document = document.UnwrapRef(id);
                count++;
            }
            catch (ProseEditException e) when (e.Failure == ProseEditFailure.TargetNotFound)
            {
                // The marker is not in this body (a pending draft's, or removed already): nothing to unwrap.
            }
        }

        result = document.ToString();
        return count;
    }

    private async Task WriteDetailsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, SourceDetailsDto before, SourceDetailsRequest d,
        CancellationToken cancellationToken, EventProvenance? provenance)
    {
        var title = d.Title?.Trim() ?? string.Empty;
        var authors = SourceAuthors.ToJson(d.AuthorsText ?? string.Empty);
        var accessed = AccessedOnDate.Parse(d.AccessedOn, DisplayDate.Today(clock.GetUtcNow(), clock.LocalTimeZone));

        if (d.Year is < 0 or > 3000)
        {
            throw ApiException.Validation("The year must be between 0 and 3000.");
        }

        var after = before with
        {
            Kind = d.Kind, Title = title, AuthorsText = SourceAuthors.ToText(authors), Year = d.Year, Publisher = Clean(d.Publisher),
            Edition = Clean(d.Edition), Isbn = Clean(d.Isbn)?.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal),
            Doi = Clean(d.Doi), Url = Clean(d.Url), AccessedOn = accessed?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Licence = d.Licence, RightsHolder = Clean(d.RightsHolder),
        };
        var changedFields = Differences(before, after);
        if (changedFields.Count == 0)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE reference SET kind = @Kind, title = @Title, authors = @authors::jsonb, year = @Year, publisher = @Publisher,
                edition = @Edition, isbn = @Isbn, doi = @Doi, url = @Url, accessed_on = @accessed, licence = @Licence,
                rights_holder = @RightsHolder
            WHERE id = @sourceId
            """,
            new { sourceId, after.Kind, after.Title, authors, after.Year, after.Publisher, after.Edition, after.Isbn, after.Doi, after.Url, accessed, after.Licence, after.RightsHolder },
            transaction, cancellationToken: cancellationToken));
        await RecordAsync(connection, transaction, sourceId, SourceEvents.DetailsEdited, new { fields = string.Join(", ", changedFields) }, cancellationToken, provenance);

        // A change to what the citation prints reopens a sign-off, as a new use does.
        if (changedFields.Any(CitedFields.Contains) && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "UPDATE reference SET signed_off_at = NULL, signed_off_by = NULL WHERE id = @sourceId AND signed_off_at IS NOT NULL RETURNING true",
                new { sourceId }, transaction, cancellationToken: cancellationToken)))
        {
            await RecordAsync(connection, transaction, sourceId, SourceEvents.Reopened, new { why = "details changed since sign-off" }, cancellationToken, provenance);
        }
    }

    private static List<string> Differences(SourceDetailsDto a, SourceDetailsDto b)
    {
        var fields = new List<string>();
        void Check<T>(string name, T x, T y)
        {
            if (!EqualityComparer<T>.Default.Equals(x, y))
            {
                fields.Add(name);
            }
        }

        Check("kind", a.Kind, b.Kind);
        Check("title", a.Title, b.Title);
        Check("authors", a.AuthorsText, b.AuthorsText);
        Check("year", a.Year, b.Year);
        Check("publisher", a.Publisher, b.Publisher);
        Check("edition", a.Edition, b.Edition);
        Check("ISBN", a.Isbn, b.Isbn);
        Check("DOI", a.Doi, b.Doi);
        Check("URL", a.Url, b.Url);
        Check("accessed date", a.AccessedOn, b.AccessedOn);
        Check("licence", a.Licence, b.Licence);
        Check("rights holder", a.RightsHolder, b.RightsHolder);
        return fields;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task UpsertCheckAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, string field, string value, string status,
        string? resolvedUrl, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO reference_check (reference_id, field, checked_value, status, resolved_url, provider, checked_at)
            VALUES (@sourceId, @field, @value, @status, @resolvedUrl, 'lookup', now())
            ON CONFLICT (reference_id, field) DO UPDATE
                SET checked_value = EXCLUDED.checked_value, status = EXCLUDED.status, http_status = NULL,
                    resolved_url = EXCLUDED.resolved_url, provider = EXCLUDED.provider, checked_at = now()
            """,
            new { sourceId, field, value, status, resolvedUrl }, transaction, cancellationToken: cancellationToken));

    private async Task RecordAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, string action, object detail,
        CancellationToken cancellationToken, EventProvenance? provenance = null) =>
        await SourceEvents.RecordAsync(connection, transaction, sourceId, currentUser.Id, action, detail, cancellationToken, provenance);

    private static async Task<string> StatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT c.status FROM reference_clearance c JOIN reference r ON r.id = c.reference_id WHERE c.reference_id = @sourceId AND r.archived_at IS NULL FOR UPDATE OF r",
            new { sourceId }, transaction, cancellationToken: ct))
        ?? throw ApiException.NotFound($"Source {sourceId} was not found.");

    private sealed record MergeRow(Guid Id, Guid BookId, string Title, bool LivePermission);

    private sealed record UseKindRow(Guid ReferenceId, string Kind);

    private sealed record UseRow(Guid Id, Guid SceneId, Guid? GenerationId);

    private Task<Guid?> RequireSourceAsync(Guid sourceId, CancellationToken cancellationToken)
        => access.RequireAsync(BookItem.Source, sourceId, BookRole.Write, cancellationToken, $"Source {sourceId} was not found.");
}
