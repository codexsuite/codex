using System.Data.Common;
using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Citations;

// Citing a source from the prose (Write's `/` → Source reference, MCP's cite_source) and removing a marker. Each is
// one IProseStore write: the reference_use row, the prose and the number snapshot share its transaction, so a stale
// version (409), an unknown scene or source (404), a source from another book or a passage that is not in the body
// (400) leaves nothing behind. `origin` is the IProseStore origin ("editor", "mcp", ...).
public interface ICitationCommands
{
    Task<CitePassageResult> CitePassageAsync(CitePassageRequest request, string origin, CancellationToken cancellationToken);

    // Cites an open quote flag (the References inbox's Cite, MCP's cite_source from a flag): its excerpt is wrapped in
    // the scene's current prose and the flag is marked cited with the new use, in the same transaction. Unknown flag
    // 404; a flag already resolved 409; no source given and none guessed 400; the CitePassage refusals otherwise.
    Task<CitePassageResult> CiteFlagAsync(Guid flagId, CiteFlagRequest request, string origin, CancellationToken cancellationToken);

    // Unwraps every link of the use; the prose triggers archive it. A use whose link is only in a pending AI draft is
    // refused (400): applying or discarding the draft settles it.
    Task<RemoveCitationResult> RemoveAsync(Guid useId, RemoveCitationRequest request, string origin, CancellationToken cancellationToken);

    // Moves the use's marker onto new words in the same scene: the old links come off and the passage is wrapped with the
    // same reference_use (so its kind, locator and history stay), in one prose write. Works on a use whose marker is gone
    // (archived); the prose triggers bring it back. A use that is only in a pending AI draft is refused (400); a stale
    // version is 409; a passage that is not in the prose is 400.
    Task<CitePassageResult> RewrapAsync(Guid useId, RewrapCitationRequest request, string origin, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class CitationCommands(NpgsqlDataSource dataSource, IProseStore prose, ICurrentUser currentUser, IBookAccess access) : ICitationCommands
{
    private const int MaxPassage = 2000;
    private const int MaxLocator = 200;

    private static readonly HashSet<string> Kinds = ["quote", "paraphrase", "data", "figure", "lyric"];

    private sealed record Clearance(Guid Id, int? Number, string Status, string Reason);

    private sealed record UseRow(Guid SceneId, Guid BookId, Guid ReferenceId, Guid? GenerationId);

    private sealed record RewrapRow(Guid SceneId, Guid ReferenceId, Guid? GenerationId);

    private sealed record FlagRow(Guid SceneId, string Excerpt, Guid? ReferenceId, string? Locator, string Status, int Version, string Body);

    public Task<CitePassageResult> CitePassageAsync(CitePassageRequest request, string origin, CancellationToken cancellationToken) =>
        CiteAsync(request, origin, null, cancellationToken);

    public async Task<CitePassageResult> CiteFlagAsync(Guid flagId, CiteFlagRequest request, string origin, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.QuoteFlag, flagId, BookRole.Write, cancellationToken, $"Quote flag {flagId} was not found.");
        FlagRow flag;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            flag = await connection.QuerySingleOrDefaultAsync<FlagRow>(new CommandDefinition(
                """
                SELECT f.scene_id AS SceneId, f.excerpt AS Excerpt, f.reference_id AS ReferenceId, f.locator AS Locator,
                       f.status AS Status, coalesce(p.version, 0)::integer AS Version, coalesce(p.body, '') AS Body
                FROM quote_flag f
                LEFT JOIN prose p ON p.scene_id = f.scene_id
                WHERE f.id = @flagId
                """,
                new { flagId }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound($"Quote flag {flagId} was not found.");
        }

        if (flag.Status != "open")
        {
            throw ApiException.Conflict("This quote has already been resolved.");
        }

        var sourceId = request.SourceId ?? flag.ReferenceId ?? throw ApiException.Validation("Say which source this quote comes from.");
        // The excerpt is plain text; the words are found in the Markdown read at this version (a later save makes the
        // write a 409), and cited as that range by passage and occurrence.
        var (start, end) = FlagPassage.Locate(flag.Body, flag.Excerpt)
            ?? throw ApiException.Validation("The flagged words are no longer in the scene's prose.");
        var passage = flag.Body[start..end];
        var occurrence = 0;
        for (var at = flag.Body.IndexOf(passage, StringComparison.Ordinal); at >= 0 && at <= start; at = flag.Body.IndexOf(passage, at + 1, StringComparison.Ordinal))
        {
            occurrence++;
        }

        var cite = new CitePassageRequest(flag.SceneId, sourceId, flag.Version, passage, occurrence, request.Kind ?? "quote", flag.Locator);
        return await CiteAsync(cite, origin, async (connection, transaction, useId, ct) =>
        {
            var resolved = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE quote_flag SET status = 'cited', resolved_use_id = @useId, resolved_by = @userId, resolved_at = now()
                WHERE id = @flagId AND status = 'open'
                """,
                new { flagId, useId, userId = currentUser.Id }, transaction, cancellationToken: ct));
            if (resolved == 0)
            {
                throw ApiException.Conflict("This quote has already been resolved.");
            }
        }, cancellationToken);
    }

    // `alsoInTransaction` runs after the prose and the use are written, before the commit (the flag's resolution).
    private async Task<CitePassageResult> CiteAsync(
        CitePassageRequest request,
        string origin,
        Func<DbConnection, DbTransaction, Guid, CancellationToken, Task>? alsoInTransaction,
        CancellationToken cancellationToken)
    {
        // The caller needs write on the scene and the source; RequireSameBookAsync then checks they share a book.
        await access.RequireAsync(BookItem.Scene, request.SceneId, BookRole.Write, cancellationToken, "Scene not found.");
        await access.RequireAsync(BookItem.Source, request.SourceId, BookRole.Write, cancellationToken, "Source not found.");
        var kind = request.Kind ?? "paraphrase";
        if (!Kinds.Contains(kind))
        {
            throw ApiException.Validation($"Unknown use kind \"{kind}\".");
        }

        if (string.IsNullOrWhiteSpace(request.Passage))
        {
            throw ApiException.Validation("The passage to cite is empty.");
        }

        if (request.Passage.Length > MaxPassage)
        {
            throw ApiException.Validation($"The passage to cite is longer than {MaxPassage} characters.");
        }

        if (request.Locator is { Length: > MaxLocator })
        {
            throw ApiException.Validation($"The locator is longer than {MaxLocator} characters.");
        }

        if (request.Occurrence is < 1)
        {
            throw ApiException.Validation("Occurrence counts from 1.");
        }

        var useId = Guid.Empty;
        var bookId = Guid.Empty;
        var body = "";
        Dictionary<Guid, Clearance> before = [];
        Dictionary<Guid, Clearance> after = [];
        var version = await prose.UpdateAsync(
            request.SceneId,
            current =>
            {
                body = Wrap(current, request, useId);
                return body;
            },
            request.Version,
            origin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    bookId = await RequireSameBookAsync(connection, transaction, request, ct);
                    before = await ClearanceAsync(connection, transaction, bookId, ct);
                    useId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                        """
                        INSERT INTO reference_use (book_id, reference_id, scene_id, kind, locator, created_by)
                        VALUES (@bookId, @sourceId, @sceneId, @kind, @locator, @userId)
                        RETURNING id
                        """,
                        new { bookId, sourceId = request.SourceId, sceneId = request.SceneId, kind, locator = request.Locator, userId = currentUser.Id },
                        transaction, cancellationToken: ct));
                },
                InTransaction = async (connection, transaction, ct) =>
                {
                    if (alsoInTransaction is not null)
                    {
                        await alsoInTransaction(connection, transaction, useId, ct);
                    }

                    after = await ClearanceAsync(connection, transaction, bookId, ct);
                },
            },
            cancellationToken);

        var cited = after[request.SourceId];
        return new CitePassageResult(
            useId,
            request.SceneId,
            version,
            body,
            cited.Number ?? 0,
            Shifted(before, after, request.SourceId),
            before.TryGetValue(request.SourceId, out var was) ? was.Status : "not_cited",
            cited.Status,
            cited.Reason);
    }

    public async Task<RemoveCitationResult> RemoveAsync(Guid useId, RemoveCitationRequest request, string origin, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ReferenceUse, useId, BookRole.Write, cancellationToken, "Source marker not found.");
        UseRow use;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            use = await connection.QuerySingleOrDefaultAsync<UseRow>(new CommandDefinition(
                """
                SELECT u.scene_id AS SceneId, u.book_id AS BookId, u.reference_id AS ReferenceId, u.generation_id AS GenerationId
                FROM reference_use u
                WHERE u.id = @useId AND u.archived_at IS NULL
                """,
                new { useId }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound("Source marker not found.");
        }

        if (use.GenerationId is not null)
        {
            throw ApiException.Validation("This marker is in a pending AI draft: apply or discard the draft instead.");
        }

        var body = "";
        Dictionary<Guid, Clearance> before = [];
        Dictionary<Guid, Clearance> after = [];
        var version = await prose.UpdateAsync(
            use.SceneId,
            current =>
            {
                body = Unwrap(current, useId);
                return body;
            },
            request.Version,
            origin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) => before = await ClearanceAsync(connection, transaction, use.BookId, ct),
                InTransaction = async (connection, transaction, ct) => after = await ClearanceAsync(connection, transaction, use.BookId, ct),
            },
            cancellationToken);

        return new RemoveCitationResult(useId, use.SceneId, version, body, Shifted(after, before, use.ReferenceId), after[use.ReferenceId].Status);
    }

    public async Task<CitePassageResult> RewrapAsync(Guid useId, RewrapCitationRequest request, string origin, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ReferenceUse, useId, BookRole.Write, cancellationToken, "Source marker not found.");
        RewrapRow use;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            use = await connection.QuerySingleOrDefaultAsync<RewrapRow>(new CommandDefinition(
                "SELECT scene_id AS SceneId, reference_id AS ReferenceId, generation_id AS GenerationId FROM reference_use WHERE id = @useId",
                new { useId }, cancellationToken: cancellationToken)) ?? throw ApiException.NotFound("Source marker not found.");
        }

        if (use.GenerationId is not null)
        {
            throw ApiException.Validation("This marker is in a pending AI draft: apply or discard the draft instead.");
        }

        if (string.IsNullOrWhiteSpace(request.Passage))
        {
            throw ApiException.Validation("The passage to cite is empty.");
        }

        if (request.Passage.Length > MaxPassage)
        {
            throw ApiException.Validation($"The passage to cite is longer than {MaxPassage} characters.");
        }

        if (request.Occurrence is < 1)
        {
            throw ApiException.Validation("Occurrence counts from 1.");
        }

        var cite = new CitePassageRequest(use.SceneId, use.ReferenceId, request.Version, request.Passage, request.Occurrence);
        var bookId = Guid.Empty;
        var body = "";
        Dictionary<Guid, Clearance> before = [];
        Dictionary<Guid, Clearance> after = [];
        var version = await prose.UpdateAsync(
            use.SceneId,
            current =>
            {
                body = Wrap(TakeOff(current, useId), cite, useId);
                return body;
            },
            request.Version,
            origin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    bookId = await RequireSameBookAsync(connection, transaction, cite, ct);
                    before = await ClearanceAsync(connection, transaction, bookId, ct);
                },
                InTransaction = async (connection, transaction, ct) => after = await ClearanceAsync(connection, transaction, bookId, ct),
            },
            cancellationToken);

        var cited = after[use.ReferenceId];
        return new CitePassageResult(
            useId,
            use.SceneId,
            version,
            body,
            cited.Number ?? 0,
            Shifted(before, after, use.ReferenceId),
            before.TryGetValue(use.ReferenceId, out var was) ? was.Status : "not_cited",
            cited.Status,
            cited.Reason);
    }

    // The body with the use's links taken off, or as it is when it has none (an archived use).
    private static string TakeOff(string current, Guid useId)
    {
        try
        {
            return ProseDocument.Parse(current).UnwrapRef(useId).Text;
        }
        catch (ProseEditException e) when (e.Failure == ProseEditFailure.TargetNotFound)
        {
            return current;
        }
    }

    private static string Wrap(string current, CitePassageRequest request, Guid useId)
    {
        var document = ProseDocument.Parse(current);
        try
        {
            if (request.Occurrence is not { } occurrence)
            {
                return document.WrapRef(request.Passage, useId).Text;
            }

            var at = -1;
            for (var n = 0; n < occurrence; n++)
            {
                at = current.IndexOf(request.Passage, at + 1, StringComparison.Ordinal);
                if (at < 0)
                {
                    throw new ProseEditException(ProseEditFailure.PassageNotWrappable, "The passage is not in the body.");
                }
            }

            return document.WrapRef(at, request.Passage.Length, useId).Text;
        }
        catch (ProseEditException)
        {
            throw ApiException.Validation("The passage is not in the scene's prose in a place a source marker can go (one paragraph, outside links and code).");
        }
    }

    private static string Unwrap(string current, Guid useId)
    {
        try
        {
            return ProseDocument.Parse(current).UnwrapRef(useId).Text;
        }
        catch (ProseEditException)
        {
            throw ApiException.NotFound("The marker is not in the scene's prose.");
        }
    }

    // The scene's book, after checking that the scene and the source exist (and are not archived) and share it.
    private static async Task<Guid> RequireSameBookAsync(DbConnection connection, DbTransaction transaction, CitePassageRequest request, CancellationToken ct)
    {
        var sceneBook = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT book_id FROM scene WHERE id = @sceneId AND archived_at IS NULL",
            new { sceneId = request.SceneId }, transaction, cancellationToken: ct)) ?? throw ApiException.NotFound("Scene not found.");
        var sourceBook = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT book_id FROM reference WHERE id = @sourceId AND archived_at IS NULL",
            new { sourceId = request.SourceId }, transaction, cancellationToken: ct)) ?? throw ApiException.NotFound("Source not found.");
        return sourceBook == sceneBook ? sceneBook : throw ApiException.Validation("The source belongs to another book.");
    }

    private static async Task<Dictionary<Guid, Clearance>> ClearanceAsync(DbConnection connection, DbTransaction transaction, Guid bookId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<Clearance>(new CommandDefinition(
            "SELECT reference_id AS Id, number::integer AS Number, status AS Status, reason AS Reason FROM reference_clearance WHERE book_id = @bookId",
            new { bookId }, transaction, cancellationToken: ct));
        return rows.ToDictionary(r => r.Id);
    }

    // How many sources other than `except` have a higher number in `later` than in `earlier`.
    private static int Shifted(Dictionary<Guid, Clearance> earlier, Dictionary<Guid, Clearance> later, Guid except) =>
        later.Values.Count(l => l.Id != except && l.Number is { } now && earlier.TryGetValue(l.Id, out var e) && e.Number is { } was && now > was);
}
