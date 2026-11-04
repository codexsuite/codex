using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.Manuscript;
using Codex.Application.Features.Print.Layout;
using Codex.Application.Features.References;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.Application.Mcp;

// The precondition of every structure step, checked against the database before undo runs any of them (#20 Safety, M14):
// the scene is still archived, the summary still reads what the agent wrote, the chapter is still where the agent put
// it, the draft is still pending. If one fails, undo returns conflict and nothing has been written, so an undo never
// lands half way or overwrites what the author did since. Prose steps are checked by McpUndo.CheckAsync (their version).
[RegisterSingleton]
public sealed class McpUndoChecks(NpgsqlDataSource dataSource, BlockCommands blocks, ISourceDetailQueries sourceDetails, ISourceExtrasCommands extras, ISourceMergeCommands sourceMerges,
    IServiceProvider services, IKbMergeCommands kbMerges, ISourceRestoreCommands sourceRestores)
{
    private sealed record SceneRow(bool Archived, string? Title, string? Summary, string Status, Guid Parent, Guid? After, string Labels, string? PlanNotes);

    private sealed record ChapterRow(string Title, string? Summary, string? Notes, bool IsNumbered);

    private sealed record PlaceRow(Guid Parent, Guid? After);

    private sealed record FigureRow(Guid AssetId, string Placement, string Caption, string AltText);

    private sealed record OpenerRow(string AssetId, string Caption);

    private sealed record CategoryRow(string Name, string? Colour);

    private sealed record EntryRow(bool Live, string Name, string? Summary, string Body, bool Flag, string Aliases, string Tags, bool Exclude);

    public async Task CheckAsync(IReadOnlyList<McpUndoStep> steps, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        foreach (var step in steps)
        {
            var problem = await ProblemAsync(connection, step, ct);
            if (problem != null)
            {
                throw new McpToolException(McpErrorCodes.Conflict, $"{problem} It changed after this write, so nothing was undone.");
            }
        }
    }

    private async Task<string?> ProblemAsync(NpgsqlConnection c, McpUndoStep s, CancellationToken ct)
    {
        switch (s.Op)
        {
            case "restore_scene":
                return (await SceneAsync(c, s.Id, ct)) is { Archived: true } ? null : "A scene this write archived is not archived any more.";
            case "archive_scene":
                return (await SceneAsync(c, s.Id, ct)) is { Archived: false } ? null : "A scene this write added or restored is not live any more.";
            case "restore_chapter":
                return await ArchivedAsync(c, "chapter", s.Id, ct) == true ? null : "A chapter this write archived is not archived any more.";
            case "archive_chapter":
                return await ArchivedAsync(c, "chapter", s.Id, ct) == false ? null : "A chapter this write added or restored is not live any more.";
            case "restore_act":
                return await ArchivedAsync(c, "act", s.Id, ct) == true ? null : "An act this write archived is not archived any more.";
            case "archive_act":
                return await ArchivedAsync(c, "act", s.Id, ct) == false ? null : "An act this write added or restored is not live any more.";
            case "patch_scene" or "set_labels" when s.Now != null:
                return SceneProblem(await SceneAsync(c, s.Id, ct), s.Now);
            case "patch_chapter" when s.Now != null:
            {
                var chapter = await c.QuerySingleOrDefaultAsync<ChapterRow>(new CommandDefinition(
                    "SELECT title, summary, notes, is_numbered AS IsNumbered FROM chapter WHERE id = @id AND archived_at IS NULL",
                    new { id = s.Id }, cancellationToken: ct));
                return chapter != null && Same(chapter.Title, s.Now.Title) && Same(chapter.Summary, s.Now.Summary) && Same(chapter.Notes, s.Now.Body)
                       && (s.Now.Flag == null || chapter.IsNumbered == s.Now.Flag)
                    ? null
                    : "A chapter this write changed no longer reads what it set.";
            }
            case "patch_act" when s.Now != null:
                return Same(await c.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT title FROM act WHERE id = @id AND archived_at IS NULL", new { id = s.Id }, cancellationToken: ct)), s.Now.Title)
                    ? null
                    : "An act this write renamed was renamed again or archived.";
            case "move_scene":
                return PlaceProblem(await PlaceAsync(c, ScenePlace, s.Id, ct), s.Now, "A scene this write moved");
            case "move_chapter":
                return PlaceProblem(await PlaceAsync(c, ChapterPlace, s.Id, ct), s.Now, "A chapter this write moved");
            case "move_act":
                return PlaceProblem(await PlaceAsync(c, ActPlace, s.Id, ct), s.Now, "An act this write moved");
            case "link_scene" or "unlink_scene":
            {
                var linked = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NULL)",
                    new { scene = s.Parent, entry = s.Id }, cancellationToken: ct));
                return linked == (s.Op == "unlink_scene") ? null : "A scene's Knowledge base link this write changed was changed again.";
            }
            case "delete_label":
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM label WHERE id = @id AND archived_at IS NULL)", new { id = s.Id }, cancellationToken: ct))
                    ? null
                    : "A label this write made is gone.";
            case "patch_beat" when s.Now != null:
                return await BeatProblemAsync(c, s, ct);
            case "set_layout" when s.Now != null:
            {
                var layout = await services.GetRequiredService<ILayoutService>().GetAsync(s.Id, ct);
                return layout.Template == s.Now.Title && layout.Trim == s.Now.Summary ? null : "A book's layout this write set was changed again.";
            }
            case "patch_entry" or "archive_entry":
                return await EntryProblemAsync(c, s, ct);
            case "patch_category" when s.Now != null:
            {
                var category = await c.QuerySingleOrDefaultAsync<CategoryRow>(new CommandDefinition(
                    "SELECT name, color AS Colour FROM kb_category WHERE id = @id", new { id = s.Id }, cancellationToken: ct));
                return category != null && (s.Now.Title == null || category.Name == s.Now.Title) && (s.Now.Summary == null || category.Colour == s.Now.Summary)
                    ? null
                    : "A Knowledge base category this write changed no longer reads what it set.";
            }
            case "move_category" when s.Now is { Place: true }:
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM kb_category WHERE id = @id AND archived_at IS NULL)", new { id = s.Id }, cancellationToken: ct))
                       && await CategoryPredecessorAsync(c, s.Id, ct) == s.Now.After
                    ? null
                    : "A Knowledge base category this write moved was moved again or deleted.";
            case "delete_category":
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    """
                    SELECT EXISTS (SELECT 1 FROM kb_category WHERE id = @id AND archived_at IS NULL)
                       AND NOT EXISTS (SELECT 1 FROM kb_entry WHERE category_id = @id AND archived_at IS NULL)
                    """,
                    new { id = s.Id }, cancellationToken: ct))
                    ? null
                    : "A Knowledge base category this write added has entries in it now, or is gone.";
            case "restore_category":
            {
                var category = await c.QuerySingleOrDefaultAsync<(string Name, bool Live, bool NameTaken)?>(new CommandDefinition(
                    """
                    SELECT k.name, k.archived_at IS NULL,
                           EXISTS (SELECT 1 FROM kb_category o WHERE o.book_id = k.book_id AND o.id <> k.id AND o.archived_at IS NULL AND lower(o.name) = lower(k.name))
                    FROM kb_category k WHERE k.id = @id
                    """,
                    new { id = s.Id }, cancellationToken: ct));
                return category switch
                {
                    null => "A Knowledge base category this write deleted is gone.",
                    { Live: true } => "A Knowledge base category this write deleted is back already.",
                    { NameTaken: true } => $"The name {category.Value.Name} is in use again, so the deleted category can't come back.",
                    _ => null,
                };
            }
            case "set_entry_category" when s.Now?.Parent is { } movedTo:
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM kb_entry WHERE id = @id AND category_id = @movedTo AND archived_at IS NULL)",
                    new { id = s.Id, movedTo }, cancellationToken: ct))
                    ? null
                    : "An entry this write moved was moved again, archived or deleted.";
            case "relate_entries" when s.Parent is { } other:
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT (SELECT count(*) FROM kb_entry WHERE id = ANY(@ids) AND archived_at IS NULL) = 2",
                    new { ids = new[] { s.Id, other } }, cancellationToken: ct))
                    ? null
                    : "An entry this write unrelated is archived or gone.";
            case "patch_source" when s.Now is { Source: not null }:
                return await sourceDetails.GetAsync(s.Id, ct) is { } source && SourceTools.State(source) == s.Now.Source
                    ? null
                    : "A source this write changed was edited again.";
            case "set_use_kind" when s.Now is { Kind: not null }:
                return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT kind FROM reference_use WHERE id = @id AND archived_at IS NULL", new { id = s.Id }, cancellationToken: ct)) == s.Now.Kind
                    ? null
                    : "A use this write retyped was changed again.";
            case "set_use_locator" when s.Now is { Body: not null }:
                return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
                    "SELECT coalesce(locator, '') FROM reference_use WHERE id = @id AND archived_at IS NULL", new { id = s.Id }, cancellationToken: ct)) == s.Now.Body
                    ? null
                    : "A use whose locator this write set was changed again.";
            case "set_source_extras" when s.Now is { Body: not null }:
                return JsonSerializer.Serialize(await extras.GetAsync(s.Id, ct), McpJson.Options) == s.Now.Body
                    ? null
                    : "A source whose cite key or permissions contact this write set was changed again.";
            case "withdraw_permission" when s.Now is { Status: not null } && s.Parent is { } sourceId:
                return (await sourceDetails.GetAsync(sourceId, ct)).Permission is { } p && p.Id == s.Id && p.Status == s.Now.Status && p.Letter == s.Now.Body
                       && p.To == s.Now.To
                    ? null
                    : "A permission request this write drafted was edited, sent or answered since.";
            case "restore_source":
                return await sourceRestores.RestoreProblemAsync(s.Id, ct);
            case "unmerge_source":
                return await sourceMerges.UnmergeProblemAsync(JsonSerializer.Deserialize<SourceMergeSnapshot>(s.Body ?? "null", McpJson.Options)!, ct);
            case "restore_entry":
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM kb_entry WHERE id = @id AND archived_at IS NOT NULL)", new { id = s.Id }, cancellationToken: ct))
                    ? null
                    : "A Knowledge base entry this write deleted is not deleted any more.";
            case "unmerge_entry":
                return await kbMerges.UnmergeProblemAsync(JsonSerializer.Deserialize<KbMergeSnapshot>(s.Body ?? "null", McpJson.Options)!, ct);
            case "patch_figure" when s.Now != null:
            {
                var figure = await c.QuerySingleOrDefaultAsync<FigureRow>(new CommandDefinition(
                    "SELECT asset_id AS AssetId, placement, coalesce(caption, '') AS Caption, coalesce(alt_text, '') AS AltText FROM figure WHERE id = @id AND archived_at IS NULL",
                    new { id = s.Id }, cancellationToken: ct));
                return figure != null && figure.AssetId == s.Now.Parent && figure.Placement == s.Now.Status && figure.Caption == s.Now.Summary
                       && figure.AltText == s.Now.Body
                    ? null
                    : "A figure this write changed was changed again or removed.";
            }
            case "set_opener" when s.Now != null:
            {
                var opener = await c.QuerySingleOrDefaultAsync<OpenerRow>(new CommandDefinition(
                    s.Title == "act"
                        ? "SELECT coalesce(opener_asset_id::text, '') AS AssetId, coalesce(opener_caption, '') AS Caption FROM act WHERE id = @id AND archived_at IS NULL"
                        : "SELECT coalesce(opener_asset_id::text, '') AS AssetId, coalesce(opener_caption, '') AS Caption FROM chapter WHERE id = @id AND archived_at IS NULL",
                    new { id = s.Id }, cancellationToken: ct));
                return opener != null && opener.AssetId == s.Now.Body && opener.Caption == s.Now.Summary
                    ? null
                    : "An opener this write set was changed again, or its act or chapter archived.";
            }
            case "discard_draft":
                return await DraftProblemAsync(c, s.Id, "pending", ct);
            case "undo_apply":
                return await DraftProblemAsync(c, s.Id, "applied", ct);
            case "restore_draft":
                return await DraftProblemAsync(c, s.Id, "discarded", ct);
            case "archive_series":
                return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM book s WHERE s.id = @id AND s.kind = 'series' AND s.archived_at IS NULL) AND NOT EXISTS (SELECT 1 FROM book WHERE series_id = @id AND deleted_at IS NULL)",
                    new { id = s.Id }, cancellationToken: ct))
                    ? null
                    : "A series this write made has books in it now, or is gone.";
            case "restore_series":
                return await ArchivedAsync(c, "series", s.Id, ct) == true ? null : "A series this write archived is not archived any more.";
            case "patch_series" when s.Now != null:
            {
                var series = await c.QuerySingleOrDefaultAsync<(string Title, string? Description)>(new CommandDefinition(
                    "SELECT title, description FROM book WHERE id = @id AND kind = 'series' AND archived_at IS NULL", new { id = s.Id }, cancellationToken: ct));
                return series.Title != null && Same(series.Title, s.Now.Title) && Same(series.Description, s.Now.Summary)
                    ? null
                    : "A series this write renamed or described was changed again or archived.";
            }
            case "set_series_order" when s.Now is { Labels: not null }:
            {
                var live = (await c.QueryAsync<Guid>(new CommandDefinition(
                    "SELECT id FROM book WHERE series_id = @id AND archived_at IS NULL ORDER BY series_number", new { id = s.Id }, cancellationToken: ct))).ToList();
                return live.SequenceEqual(s.Now.Labels) ? null : "A series' order this write set was changed again.";
            }
            default:
                return null;
        }
    }

    private static Task<SceneRow?> SceneAsync(NpgsqlConnection c, Guid id, CancellationToken ct) => c.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
        """
        SELECT s.archived_at IS NOT NULL AS Archived, s.title, s.summary, s.status, s.chapter_id AS Parent,
               (SELECT s2.id FROM scene s2 WHERE s2.chapter_id = s.chapter_id AND s2.archived_at IS NULL AND s2.position < s.position
                ORDER BY s2.position DESC LIMIT 1) AS After,
               coalesce((SELECT json_agg(sl.label_id) FROM scene_label sl JOIN label l ON l.id = sl.label_id AND l.archived_at IS NULL WHERE sl.scene_id = s.id AND sl.archived_at IS NULL), '[]'::json)::text AS Labels,
               s.plan_notes AS PlanNotes
        FROM scene s WHERE s.id = @id
        """, new { id }, cancellationToken: ct));

    // Whether the chapter or act is archived; null when it is gone.
    private static Task<bool?> ArchivedAsync(NpgsqlConnection c, string table, Guid id, CancellationToken ct) => c.ExecuteScalarAsync<bool?>(new CommandDefinition(
        table switch
        {
            "act" => "SELECT archived_at IS NOT NULL FROM act WHERE id = @id",
            "series" => "SELECT archived_at IS NOT NULL FROM book WHERE id = @id AND kind = 'series'",
            _ => "SELECT archived_at IS NOT NULL FROM chapter WHERE id = @id",
        },
        new { id }, cancellationToken: ct));

    // A row's parent and the live sibling before it.
    private const string ScenePlace =
        """
        SELECT t.chapter_id AS Parent,
               (SELECT t2.id FROM scene t2 WHERE t2.chapter_id = t.chapter_id AND t2.archived_at IS NULL AND t2.position < t.position
                ORDER BY t2.position DESC LIMIT 1) AS After
        FROM scene t WHERE t.id = @id
        """;

    private const string ChapterPlace =
        """
        SELECT t.act_id AS Parent,
               (SELECT t2.id FROM chapter t2 WHERE t2.act_id = t.act_id AND t2.archived_at IS NULL AND t2.position < t.position
                ORDER BY t2.position DESC LIMIT 1) AS After
        FROM chapter t WHERE t.id = @id
        """;

    private const string ActPlace =
        """
        SELECT t.book_id AS Parent,
               (SELECT t2.id FROM act t2 WHERE t2.book_id = t.book_id AND t2.archived_at IS NULL AND t2.position < t.position
                ORDER BY t2.position DESC LIMIT 1) AS After
        FROM act t WHERE t.id = @id
        """;

    private static Task<PlaceRow?> PlaceAsync(NpgsqlConnection c, string sql, Guid id, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<PlaceRow>(new CommandDefinition(sql, new { id }, cancellationToken: ct));

    private static string? SceneProblem(SceneRow? row, McpUndoState now)
    {
        if (row == null)
        {
            return "A scene this write changed is gone.";
        }
        bool Differs(string? a, string? b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
        if ((now.Title != null && Differs(row.Title, now.Title)) || (now.Summary != null && Differs(row.Summary, now.Summary))
            || (now.Status != null && row.Status != now.Status) || (now.Body != null && Differs(row.PlanNotes, now.Body))
            || (now.Labels != null && !JsonSerializer.Deserialize<List<Guid>>(row.Labels)!.ToHashSet().SetEquals(now.Labels)))
        {
            return "A scene's title, summary, plan notes, status or labels no longer read what this write set.";
        }
        return null;
    }

    // A null expectation is not checked; text is compared trimmed, with null reading as "".
    private static bool Same(string? actual, string? expected) =>
        expected == null || string.Equals((actual ?? "").Trim(), expected.Trim(), StringComparison.Ordinal);

    private static string? PlaceProblem(PlaceRow? row, McpUndoState? now, string what) =>
        now is not { Place: true } || (row != null && row.Parent == now.Parent && row.After == now.After) ? null : $"{what} is not where it left it.";

    private async Task<string?> BeatProblemAsync(NpgsqlConnection c, McpUndoStep s, CancellationToken ct)
    {
        var now = s.Now!;
        var bookId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT sc.book_id FROM beat b JOIN scene sc ON sc.id = b.scene_id WHERE b.id = @id AND b.archived_at IS NULL", new { id = s.Id }, cancellationToken: ct));
        var beat = bookId == null ? null : (await blocks.ListAsync(bookId.Value, ct)).Beats.SingleOrDefault(b => b.Id == s.Id);
        return beat != null && (now.Body == null || beat.Body == now.Body) && (now.Flag == null || beat.IsHidden == now.Flag)
               && (now.Labels == null || beat.ContextEntryIds.ToHashSet().SetEquals(now.Labels))
            ? null
            : "A beat this write changed no longer reads what it set.";
    }

    private static async Task<string?> EntryProblemAsync(NpgsqlConnection c, McpUndoStep s, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            "SELECT archived_at IS NULL AS Live, name, summary, body, always_in_context AS Flag, to_json(aliases)::text AS Aliases, to_json(tags)::text AS Tags, exclude_from_auto_context AS Exclude FROM kb_entry WHERE id = @id",
            new { id = s.Id }, cancellationToken: ct));
        if (row is not { Live: true })
        {
            return "A Knowledge base entry this write made or changed is archived or gone.";
        }
        var now = s.Now;
        bool Differs(string? a, string? b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
        if (now != null
            && ((now.Title != null && Differs(row.Name, now.Title)) || (now.Summary != null && Differs(row.Summary, now.Summary))
                || (now.Body != null && row.Body != now.Body) || (now.Flag != null && row.Flag != now.Flag)
                || (now.Texts != null && !JsonSerializer.Deserialize<List<string>>(row.Aliases)!.Select(a => a.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(now.Texts.Select(a => a.Trim())))
                || (now.Tags != null && !JsonSerializer.Deserialize<List<string>>(row.Tags)!.Select(a => a.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(now.Tags.Select(a => a.Trim())))
                || (now.Exclude != null && row.Exclude != now.Exclude)))
        {
            return "A Knowledge base entry this write changed no longer reads what it set.";
        }
        return null;
    }

    // The category right before this one among its book's or series' categories, null when it is first.
    private static async Task<Guid?> CategoryPredecessorAsync(NpgsqlConnection c, Guid id, CancellationToken ct) =>
        await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT p.id FROM kb_category me
            JOIN kb_category p ON p.book_id = me.book_id AND p.archived_at IS NULL AND p.id <> me.id AND (p.position, p.id) < (me.position, me.id)
            WHERE me.id = @id ORDER BY p.position DESC, p.id DESC LIMIT 1
            """,
            new { id }, cancellationToken: ct));

    private static async Task<string?> DraftProblemAsync(NpgsqlConnection c, Guid id, string status, CancellationToken ct) =>
        await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT status FROM ai_generation WHERE id = @id", new { id }, cancellationToken: ct)) == status
            ? null
            : $"A draft this write changed is no longer {status}.";
}
