using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// Find and replace over a book's text (J23): scene prose, scene summaries and beat instructions, in a scope, with the
// rules of Codex.Application.Prose.ProseSearch. Prose is written only through IProseStore (origin "manual", so each
// changed scene gets a revision, and the text it had before is kept for Undo). A pending AI draft is counted, not changed.
[RegisterSingleton]
public sealed class FindReplace(
    NpgsqlDataSource dataSource, IBookAccess access, IProseStore prose, ProseHistory history, IBookEvents events)
{
    private const int MaxItems = 200;
    private const int Attempts = 3;
    private const string ManualOrigin = "manual";

    private sealed record SceneRow(Guid Id, string? Summary, string Body, int Version);

    private sealed record BeatRow(Guid Id, Guid SceneId, string Body);

    private sealed record Searched(IReadOnlyList<SceneRow> Scenes, IReadOnlyList<BeatRow> Beats, int InDrafts);

    public async Task<FindResultDto> FindAsync(Guid bookId, FindRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        var options = Options(request.Query, request.MatchCase, request.WholeWord);
        var searched = await LoadAsync(bookId, request.ScopeKind, request.ScopeId, request.Prose, request.Beats, request.IncludeArchived, options, cancellationToken);
        var items = new List<FindItemDto>();
        var total = 0;
        var counts = new Dictionary<MatchStatus, int>();
        var scenes = new HashSet<Guid>();

        void Take(Guid sceneId, string source, IReadOnlyList<TextMatch> matches, int firstOrdinal)
        {
            foreach (var m in matches)
            {
                total++;
                counts[m.Status] = counts.GetValueOrDefault(m.Status) + 1;
                scenes.Add(sceneId);
                if (items.Count < MaxItems)
                {
                    items.Add(new FindItemDto(sceneId, source, firstOrdinal + m.Ordinal, StatusName(m.Status), m.Before, m.Text, m.After));
                }
            }
        }

        foreach (var scene in searched.Scenes)
        {
            if (request.Prose)
            {
                Take(scene.Id, "prose", ProseSearch.Find(scene.Body, SearchKind.Prose, options), 0);
            }

            if (request.Summaries && scene.Summary is not null)
            {
                Take(scene.Id, "summary", ProseSearch.Find(scene.Summary, SearchKind.Summary, options), 0);
            }

            if (request.Beats)
            {
                var ordinal = 0;
                foreach (var beat in searched.Beats.Where(b => b.SceneId == scene.Id))
                {
                    var matches = ProseSearch.Find(beat.Body, SearchKind.Plain, options);
                    Take(scene.Id, "beat", matches, ordinal);
                    ordinal += matches.Count;
                }
            }
        }

        return new FindResultDto(
            total, counts.GetValueOrDefault(MatchStatus.Replaceable), counts.GetValueOrDefault(MatchStatus.Cited),
            counts.GetValueOrDefault(MatchStatus.SpansLink), searched.InDrafts, scenes.Count, total > items.Count, items);
    }

    public async Task<ReplaceResultDto> ReplaceAsync(Guid bookId, ReplaceRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, "Book not found.");
        var options = Options(request.Query, request.MatchCase, request.WholeWord);
        if (request.Replacement == request.Query)
        {
            throw ApiException.Validation("The replacement is the same as the search text.");
        }

        // One match on its own: the scope is that scene, and only the source it names is searched.
        var only = request.Only;
        var kind = only is null ? request.ScopeKind : "scene";
        var scopeId = only?.SceneId ?? request.ScopeId;
        var doProse = only is null ? request.Prose : only.Source == "prose";
        var doSummaries = only is null ? request.Summaries : only.Source == "summary";
        var doBeats = only is null ? request.Beats : only.Source == "beat";

        var proseUndo = new List<ReplaceUndoProse>();
        var summaryUndo = new List<ReplaceUndoSummary>();
        var beatUndo = new List<ReplaceUndoBeat>();
        var replaced = 0;
        var cited = 0;
        var spans = 0;
        var scenesChanged = new HashSet<Guid>();

        if (doProse)
        {
            var result = await ReplaceProseAsync(bookId, kind, scopeId, request, options, only, cancellationToken);
            replaced += result.Replaced;
            cited += result.Cited;
            spans += result.Spans;
            proseUndo.AddRange(result.Undo);
            scenesChanged.UnionWith(result.Undo.Select(u => u.SceneId));
        }

        if (doSummaries || doBeats)
        {
            var searched = await LoadAsync(bookId, kind, scopeId, false, doBeats, request.IncludeArchived, options, cancellationToken);
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            if (doSummaries)
            {
                foreach (var scene in searched.Scenes.Where(s => s.Summary is not null))
                {
                    var outcome = ProseSearch.Replace(scene.Summary!, SearchKind.Summary, options, request.Replacement, OrdinalFor(only, "summary", scene.Id));
                    cited += outcome.Cited;
                    spans += outcome.Spans;
                    if (outcome.Replaced == 0)
                    {
                        continue;
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE scene SET summary = nullif(@text, '') WHERE id = @id", new { text = outcome.Text, id = scene.Id }, cancellationToken: cancellationToken));
                    summaryUndo.Add(new ReplaceUndoSummary(scene.Id, scene.Summary));
                    replaced += outcome.Replaced;
                    scenesChanged.Add(scene.Id);
                }
            }

            if (doBeats)
            {
                foreach (var group in searched.Beats.GroupBy(b => b.SceneId))
                {
                    var before = 0;
                    foreach (var beat in group)
                    {
                        var all = ProseSearch.Find(beat.Body, SearchKind.Plain, options).Count;
                        int? ordinal = null;
                        if (only is { Source: "beat" })
                        {
                            ordinal = only.Ordinal - before;
                        }

                        before += all;
                        if (ordinal is < 0 || ordinal >= all)
                        {
                            continue;
                        }

                        var outcome = ProseSearch.Replace(beat.Body, SearchKind.Plain, options, request.Replacement, ordinal);
                        if (outcome.Replaced == 0)
                        {
                            continue;
                        }

                        await connection.ExecuteAsync(new CommandDefinition(
                            "UPDATE beat SET body = @text, updated_at = now() WHERE id = @id", new { text = outcome.Text, id = beat.Id }, cancellationToken: cancellationToken));
                        beatUndo.Add(new ReplaceUndoBeat(beat.Id, beat.Body));
                        replaced += outcome.Replaced;
                        scenesChanged.Add(group.Key);
                    }
                }
            }

            if (summaryUndo.Count > 0 || beatUndo.Count > 0)
            {
                await events.PublishAsync(new StructureChanged(bookId, "scene"));
            }
        }

        var drafts = doProse && only is null
            ? (await LoadAsync(bookId, kind, scopeId, true, false, request.IncludeArchived, options, cancellationToken)).InDrafts
            : 0;
        return new ReplaceResultDto(replaced, scenesChanged.Count, cited, spans, drafts, new ReplaceUndoDto(proseUndo, summaryUndo, beatUndo));
    }

    // Takes a replace back as one action: each changed scene returns to the revision it had (an ordinary restore, so
    // history only grows), and each summary and beat instruction to its old text.
    public async Task<int> UndoAsync(Guid bookId, ReplaceUndoDto undo, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, "Book not found.");
        var restored = 0;
        foreach (var item in undo.Prose)
        {
            try
            {
                await history.RestoreAsync(item.SceneId, item.RevisionId, cancellationToken);
                restored++;
            }
            catch (ApiException e) when (e.Code is ApiErrorCodes.Validation or ApiErrorCodes.Blocked)
            {
                // Already back to that text, or the scene has since been archived: nothing to undo there.
            }
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var item in undo.Summaries)
        {
            restored += await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET summary = @text WHERE id = @id AND book_id = @bookId", new { text = item.Text, id = item.SceneId, bookId }, cancellationToken: cancellationToken)) > 0 ? 1 : 0;
        }

        foreach (var item in undo.Beats)
        {
            restored += await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE beat b SET body = @text, updated_at = now() FROM scene s WHERE b.id = @id AND s.id = b.scene_id AND s.book_id = @bookId",
                new { text = item.Text, id = item.BeatId, bookId }, cancellationToken: cancellationToken)) > 0 ? 1 : 0;
        }

        if (undo.Summaries.Count > 0 || undo.Beats.Count > 0)
        {
            await events.PublishAsync(new StructureChanged(bookId, "scene"));
        }

        return restored;
    }

    private async Task<(int Replaced, int Cited, int Spans, List<ReplaceUndoProse> Undo)> ReplaceProseAsync(
        Guid bookId, string scopeKind, Guid? scopeId, ReplaceRequest request, SearchOptions options, ReplaceTarget? only, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var searched = await LoadAsync(bookId, scopeKind, scopeId, true, false, request.IncludeArchived, options, cancellationToken);
            var changing = searched.Scenes
                .Where(s => ProseSearch.Replace(s.Body, SearchKind.Prose, options, request.Replacement, OrdinalFor(only, "prose", s.Id)).Replaced > 0)
                .ToList();
            var outcomes = new Dictionary<Guid, (string Before, ReplaceOutcome Outcome)>();
            var skipped = searched.Scenes.Except(changing).Select(s => ProseSearch.Replace(s.Body, SearchKind.Prose, options, request.Replacement, OrdinalFor(only, "prose", s.Id))).ToList();
            var undo = new List<ReplaceUndoProse>();
            try
            {
                if (changing.Count > 0)
                {
                    var edits = changing.Select(scene => (scene.Id, scene.Version, (Func<string, string>)(current =>
                    {
                        var outcome = ProseSearch.Replace(current, SearchKind.Prose, options, request.Replacement, OrdinalFor(only, "prose", scene.Id));
                        outcomes[scene.Id] = (current, outcome);
                        return outcome.Text;
                    }))).ToList();
                    await prose.UpdateManyAsync(
                        edits, ManualOrigin,
                        new ProseWriteOptions
                        {
                            InTransaction = async (connection, transaction, ct) =>
                            {
                                foreach (var (sceneId, (before, _)) in outcomes)
                                {
                                    undo.Add(new ReplaceUndoProse(sceneId, await ProseHistory.KeepPreviousAsync(connection, transaction, sceneId, before, ct)));
                                }
                            },
                        },
                        cancellationToken);
                }
            }
            catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict && attempt < Attempts)
            {
                continue;
            }

            var done = outcomes.Values.Select(v => v.Outcome).Concat(skipped).ToList();
            return (done.Sum(o => o.Replaced), done.Sum(o => o.Cited), done.Sum(o => o.Spans), undo);
        }

        throw ApiException.Conflict("The manuscript keeps changing. Try again.");
    }

    // null: every match. With a single target, the target's ordinal in its own scene and source, and -1 (no match) elsewhere.
    private static int? OrdinalFor(ReplaceTarget? only, string source, Guid sceneId)
    {
        if (only is null)
        {
            return null;
        }

        return only.Source == source && only.SceneId == sceneId ? only.Ordinal : -1;
    }

    private static SearchOptions Options(string query, bool matchCase, bool wholeWord)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw ApiException.Validation("Search text is required.");
        }

        return new SearchOptions(query, matchCase, wholeWord);
    }

    private static string StatusName(MatchStatus status) => status switch
    {
        MatchStatus.Cited => "cited",
        MatchStatus.SpansLink => "spans_link",
        _ => "replaceable",
    };

    private async Task<Searched> LoadAsync(
        Guid bookId, string scopeKind, Guid? scopeId, bool prose, bool beats, bool includeArchived, SearchOptions options, CancellationToken cancellationToken)
    {
        if (scopeKind is not ("book" or "act" or "chapter" or "scene") || (scopeKind != "book" && scopeId is null))
        {
            throw ApiException.Validation("The scope is not valid.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var scenes = (await connection.QueryAsync<SceneRow>(new CommandDefinition(
            """
            SELECT s.id AS Id, s.summary AS Summary, coalesce(p.body, '') AS Body, coalesce(p.version, 0)::int AS Version
            FROM scene s
            JOIN chapter c ON c.id = s.chapter_id
            JOIN act a ON a.id = c.act_id
            LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @bookId
              AND (@includeArchived OR (s.archived_at IS NULL AND c.archived_at IS NULL AND a.archived_at IS NULL))
              AND (@scopeKind = 'book'
                   OR (@scopeKind = 'act' AND a.id = @scopeId)
                   OR (@scopeKind = 'chapter' AND c.id = @scopeId)
                   OR (@scopeKind = 'scene' AND s.id = @scopeId))
            ORDER BY a.position, c.position, c.id, s.position, s.id
            """,
            new { bookId, includeArchived, scopeKind, scopeId }, cancellationToken: cancellationToken))).ToList();
        var ids = scenes.Select(s => s.Id).ToArray();

        IReadOnlyList<BeatRow> beatRows = [];
        if (beats)
        {
            beatRows = (await connection.QueryAsync<BeatRow>(new CommandDefinition(
                "SELECT id AS Id, scene_id AS SceneId, body AS Body FROM beat WHERE scene_id = ANY(@ids) AND archived_at IS NULL ORDER BY created_at, id",
                new { ids }, cancellationToken: cancellationToken))).ToList();
        }

        var inDrafts = 0;
        if (prose)
        {
            var outputs = await connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT output FROM (
                    SELECT DISTINCT ON (coalesce(beat_id, scene_id)) output, status
                    FROM ai_generation
                    WHERE scene_id = ANY(@ids) AND output IS NOT NULL
                    ORDER BY coalesce(beat_id, scene_id), created_at DESC, id DESC
                ) latest WHERE status = 'pending'
                """,
                new { ids }, cancellationToken: cancellationToken));
            inDrafts = outputs.Sum(o => ProseSearch.Find(o, SearchKind.Prose, options).Count);
        }

        return new Searched(scenes, beatRows, inDrafts);
    }
}
