using System.Data.Common;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Import;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.Core;
using Dapper;

namespace Codex.Application.Features.Import;

// Step three of the pipeline: writes an ImportPlan as a new book in one transaction through IProseStore, so the book,
// its structure, beats, entries, prose (with one 'import' revision per scene) and the book_import row commit together
// or not at all; so do the asset rows of images already stored (ImportAssets), the figures and openers placing them.
// Undo import deletes the book, and everything here cascades from it.
[RegisterSingleton]
public sealed class ImportWriter(IProseStore proseStore, ICurrentUser currentUser, IAssetIntake assetIntake)
{
    public const string Origin = "import";

    private const string DefaultActPosition = "a0";

    public async Task<ImportSummaryDto> WriteAsync(string source, ImportPlan plan, ImportPreviewDto mapping, ImportAssets assets, CancellationToken cancellationToken)
    {
        var importId = Guid.CreateVersion7();
        var bookId = assets.BookId;
        var rows = Rows.From(plan);
        ImportSummaryDto? summary = null;

        var edits = rows.Scenes
            .Where(s => s.Body.Length > 0)
            .Select(s => (s.Id, 0, (Func<string, string>)(_ => s.Body)))
            .ToList();
        var options = new ProseWriteOptions
        {
            BeforeWrites = (connection, transaction, ct) => InsertRowsAsync(connection, transaction, importId, bookId, source, plan, rows, mapping, assets, ct),
            InTransaction = async (connection, transaction, ct) =>
                summary = await FinishAsync(connection, transaction, importId, bookId, source, plan, ct),
        };
        await proseStore.UpdateManyAsync(edits, Origin, options, cancellationToken);
        return summary!;
    }

    private async Task InsertRowsAsync(
        DbConnection connection, DbTransaction transaction, Guid importId, Guid bookId, string source, ImportPlan plan, Rows rows,
        ImportPreviewDto mapping, ImportAssets assets, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book (id, owner_id, title, subtitle, author_name, description, style_guide)
            VALUES (@bookId, @owner, @Title, @Subtitle, @Author, @Description, @StyleGuide)
            """,
            new { bookId, owner = currentUser.Id, plan.Book.Title, plan.Book.Subtitle, plan.Book.Author, plan.Book.Description, plan.Book.StyleGuide },
            transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book_import (id, book_id, source, file_name, status, mapping, created_by)
            VALUES (@importId, @bookId, @source, @fileName, 'importing', @mapping::jsonb, @owner)
            """,
            new { importId, bookId, source, fileName = plan.FileName, mapping = JsonSerializer.Serialize(mapping, JsonSerializerOptions.Web), owner = currentUser.Id },
            transaction, cancellationToken: ct));

        foreach (var asset in assets.ByPath.Values)
        {
            await assetIntake.InsertAsync(connection, transaction, asset, ct);
        }

        // The book trigger made an untitled act at 'a0': the first imported act takes it over.
        var actId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM act WHERE book_id = @bookId AND position = @position",
            new { bookId, position = DefaultActPosition }, transaction, cancellationToken: ct));
        string? actKey = null;
        foreach (var act in rows.Acts)
        {
            var opener = act.Plan.Opener is { } art && assets.ByPath.TryGetValue(art.ImagePath, out var image) ? image.Id : (Guid?)null;
            var openerCaption = opener == null ? null : act.Plan.Opener!.Caption;
            if (actKey == null)
            {
                actKey = DefaultActPosition;
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE act SET title = @Title, summary = @Summary, opener_asset_id = @opener, opener_caption = @openerCaption WHERE id = @actId",
                    new { act.Plan.Title, act.Plan.Summary, opener, openerCaption, actId }, transaction, cancellationToken: ct));
            }
            else
            {
                actKey = FractionalKey.Between(actKey, null);
                actId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    """
                    INSERT INTO act (book_id, position, title, summary, opener_asset_id, opener_caption)
                    VALUES (@bookId, @actKey, @Title, @Summary, @opener, @openerCaption) RETURNING id
                    """,
                    new { bookId, actKey, act.Plan.Title, act.Plan.Summary, opener, openerCaption }, transaction, cancellationToken: ct));
            }
            await InsertChaptersAsync(connection, transaction, bookId, actId, act.Chapters, ct);
        }

        await InsertFiguresAsync(connection, transaction, rows, assets, ct);
        await InsertEntriesAsync(connection, transaction, bookId, plan, rows, ct);
    }

    // Live from the start: the prose written after BeforeWrites holds each one's ::figure line.
    private static async Task InsertFiguresAsync(DbConnection connection, DbTransaction transaction, Rows rows, ImportAssets assets, CancellationToken ct)
    {
        var figures = rows.Scenes
            .SelectMany(scene => (scene.Plan.Figures ?? []).Select(f => (Scene: scene.Id, Figure: f, Asset: assets.ByPath[f.ImagePath].Id)))
            .ToList();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO figure (id, scene_id, asset_id, placement, caption)
            SELECT f.id, f.scene_id, f.asset_id, f.placement, f.caption FROM unnest(@ids, @scenes, @assetIds, @placements, @captions)
                AS f (id, scene_id, asset_id, placement, caption)
            """,
            new
            {
                ids = figures.Select(f => f.Figure.Id).ToArray(),
                scenes = figures.Select(f => f.Scene).ToArray(),
                assetIds = figures.Select(f => f.Asset).ToArray(),
                placements = figures.Select(f => f.Figure.Placement).ToArray(),
                captions = figures.Select(f => f.Figure.Caption).ToArray(),
            },
            transaction, cancellationToken: ct));
    }

    private static async Task InsertChaptersAsync(
        DbConnection connection, DbTransaction transaction, Guid bookId, Guid actId, IReadOnlyList<ChapterRows> chapters, CancellationToken ct)
    {
        string? chapterKey = null;
        foreach (var chapter in chapters)
        {
            chapterKey = FractionalKey.Between(chapterKey, null);
            var chapterId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO chapter (book_id, act_id, position, title, summary, notes, is_numbered)
                VALUES (@bookId, @actId, @chapterKey, @Title, @Summary, @Notes, @IsNumbered) RETURNING id
                """,
                new { bookId, actId, chapterKey, chapter.Plan.Title, chapter.Plan.Summary, chapter.Plan.Notes, chapter.Plan.IsNumbered },
                transaction, cancellationToken: ct));
            string? sceneKey = null;
            foreach (var scene in chapter.Scenes)
            {
                sceneKey = FractionalKey.Between(sceneKey, null);
                var p = scene.Plan;
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO scene (id, book_id, chapter_id, position, title, summary, plan_notes, status, import_ref, archived_at)
                    VALUES (@Id, @bookId, @chapterId, @sceneKey, @Title, @Summary, @PlanNotes, @Status, @ImportRef,
                            CASE WHEN @Archived THEN now() END)
                    """,
                    new { scene.Id, bookId, chapterId, sceneKey, p.Title, p.Summary, p.PlanNotes, p.Status, p.ImportRef, p.Archived },
                    transaction, cancellationToken: ct));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO beat (id, scene_id, body) SELECT b.id, @sceneId, b.body FROM unnest(@ids, @bodies) AS b (id, body)",
                    new { sceneId = scene.Id, ids = scene.BeatIds.ToArray(), bodies = p.Beats.ToArray() }, transaction, cancellationToken: ct));
            }
        }
    }

    // Categories one by one (there are few); entries and scene links in one statement each, so a large codex[] stays fast.
    private static async Task InsertEntriesAsync(DbConnection connection, DbTransaction transaction, Guid bookId, ImportPlan plan, Rows rows, CancellationToken ct)
    {
        var categories = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var lastPosition = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? categoryKey = null;
        var entryIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var ids = new List<Guid>();
        var categoryIds = new List<Guid>();
        var positions = new List<string>();
        foreach (var entry in plan.Entries)
        {
            if (!categories.TryGetValue(entry.Category, out var categoryId))
            {
                categoryKey = FractionalKey.Between(categoryKey, null);
                var described = plan.Categories?.FirstOrDefault(c => c.Name == entry.Category);
                var fieldDefs = JsonSerializer.Serialize(described?.Fields.Select(f => new { key = f.Key, label = f.Label }) ?? [], JsonSerializerOptions.Web);
                categoryId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    """
                    INSERT INTO kb_category (book_id, name, tag, position, field_defs)
                    VALUES (@bookId, @Category, @tag, @categoryKey, @fieldDefs::jsonb) RETURNING id
                    """,
                    new { bookId, entry.Category, tag = described?.Tag, categoryKey, fieldDefs }, transaction, cancellationToken: ct));
                categories[entry.Category] = categoryId;
            }
            var position = FractionalKey.Between(lastPosition.GetValueOrDefault(entry.Category), null);
            lastPosition[entry.Category] = position;
            var id = entry.Id ?? Guid.CreateVersion7();
            entryIds[entry.Key] = id;
            ids.Add(id);
            categoryIds.Add(categoryId);
            positions.Add(position);
        }
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO kb_entry (id, book_id, category_id, name, position, summary, body, external_id, aliases, fields, import_url, always_in_context)
            SELECT e.id, @bookId, e.category_id, e.name, e.position, e.summary, coalesce(e.body, ''), e.external_id,
                   ARRAY(SELECT jsonb_array_elements_text(e.aliases::jsonb)), e.fields::jsonb, e.import_url, e.always_in_context
            FROM unnest(@ids, @categoryIds, @names, @positions, @summaries, @bodies, @externalIds, @aliases, @fields, @importUrls, @alwaysInContext)
                 AS e (id, category_id, name, position, summary, body, external_id, aliases, fields, import_url, always_in_context)
            """,
            new
            {
                bookId,
                ids = ids.ToArray(),
                categoryIds = categoryIds.ToArray(),
                names = plan.Entries.Select(e => e.Name).ToArray(),
                positions = positions.ToArray(),
                summaries = plan.Entries.Select(e => e.Summary).ToArray(),
                bodies = plan.Entries.Select(e => e.Body).ToArray(),
                externalIds = plan.Entries.Select(e => e.ExternalId).ToArray(),
                aliases = plan.Entries.Select(e => JsonSerializer.Serialize(e.Aliases ?? [], JsonSerializerOptions.Web)).ToArray(),
                fields = plan.Entries.Select(e => JsonSerializer.Serialize(e.Fields ?? new Dictionary<string, string>())).ToArray(),
                importUrls = plan.Entries.Select(e => e.ImportUrl).ToArray(),
                alwaysInContext = plan.Entries.Select(e => e.AlwaysInContext).ToArray(),
            },
            transaction, cancellationToken: ct));

        var links = rows.Scenes
            .SelectMany(scene => scene.Plan.Entries.Where(entryIds.ContainsKey).Distinct().Select(key => (Scene: scene.Id, Entry: entryIds[key])))
            .ToList();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT l.scene_id, l.entry_id FROM unnest(@scenes, @entries) AS l (scene_id, entry_id)",
            new { scenes = links.Select(l => l.Scene).ToArray(), entries = links.Select(l => l.Entry).ToArray() },
            transaction, cancellationToken: ct));
    }

    private static async Task<ImportSummaryDto> FinishAsync(
        DbConnection connection, DbTransaction transaction, Guid importId, Guid bookId, string source, ImportPlan plan, CancellationToken ct)
    {
        var counts = await connection.QuerySingleAsync<(int Acts, int Chapters, int Scenes, int Words, int Entries)>(new CommandDefinition(
            """
            SELECT (SELECT count(*)::int FROM act WHERE book_id = @bookId),
                   (SELECT count(*)::int FROM chapter WHERE book_id = @bookId),
                   (SELECT count(*)::int FROM scene WHERE book_id = @bookId AND archived_at IS NULL),
                   (SELECT coalesce(sum(p.word_count), 0)::int FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @bookId),
                   (SELECT count(*)::int FROM kb_entry WHERE book_id = @bookId)
            """,
            new { bookId }, transaction, cancellationToken: ct));
        var summary = new ImportSummaryDto(
            importId, bookId, source, plan.FileName, counts.Acts, counts.Chapters, counts.Scenes, counts.Words, counts.Entries,
            plan.Skipped.Select(s => new ImportSkippedDto(s.Path, s.Reason)).ToList());
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE book_import SET status = 'done', result = @result::jsonb, finished_at = now() WHERE id = @importId",
            new { importId, result = JsonSerializer.Serialize(summary, JsonSerializerOptions.Web) }, transaction, cancellationToken: ct));
        return summary;
    }

    // Ids picked up front: the prose edits name their scenes before BeforeWrites inserts them.
    private sealed record Rows(IReadOnlyList<ActRows> Acts, IReadOnlyList<SceneRows> Scenes)
    {
        public static Rows From(ImportPlan plan)
        {
            var acts = plan.Acts.Select(a => new ActRows(a, a.Chapters.Select(c => new ChapterRows(c, c.Scenes.Select(SceneRows.From).ToList())).ToList())).ToList();
            return new Rows(acts, acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).ToList());
        }
    }

    private sealed record ActRows(ImportActPlan Plan, IReadOnlyList<ChapterRows> Chapters);

    private sealed record ChapterRows(ImportChapterPlan Plan, IReadOnlyList<SceneRows> Scenes);

    private sealed record SceneRows(Guid Id, ImportScenePlan Plan, IReadOnlyList<Guid> BeatIds, string Body)
    {
        // Beat lines at the top of the body, then the prose (issue #10's mapping).
        public static SceneRows From(ImportScenePlan plan)
        {
            var beatIds = plan.Beats.Select(_ => Guid.CreateVersion7()).ToList();
            var blocks = beatIds.Select(id => $"::beat{{id=\"{id}\"}}").ToList();
            if (plan.Prose != null)
            {
                blocks.Add(plan.Prose.TrimEnd('\n'));
            }
            return new SceneRows(Guid.CreateVersion7(), plan, beatIds, blocks.Count == 0 ? "" : string.Join("\n\n", blocks) + "\n");
        }
    }
}
