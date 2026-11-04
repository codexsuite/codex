using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agoda.IoC.Core;
using Codex.Contracts.Import;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// The source export / backup (Settings → Archive & export, J20): a .zip Codex can import again (Library → Import → Codex
// backup). Format version 1:
//   book.json            the book's fields, series name and number, publication settings (citation style, quote limit, ...)
//   structure.json       acts, chapters, scenes (each with the `file` that holds its prose), beats, and the scene and beat
//                        links to Knowledge base entries; table-shaped (column names as the database has them)
//   manuscript/act-01-<slug>/chapter-01-<slug>/scene-01.md   one file per live scene, exactly as stored: beats, additions,
//                        `[passage](ref:<use id>)` and `[name](kb:<id>)` links kept
//   knowledge-base.json  categories, entries (aliases, fields), related links, scene additions
//   references.json      sources, their uses with ids, permission requests, sign-offs (on the source) and quote flags
//   labels.json          labels and the scenes that carry them
//   manifest.json        title, author, format version and the scene files in order
// Live rows only. Left out: archived rows, prose history, pending AI drafts and their source uses, images and other files,
// users (restored rows belong to whoever imports), layout settings, link-check results and the source history.
public interface ISourceArchive
{
    Task<SourceArchiveResult> BuildAsync(Guid bookId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class SourceArchive(NpgsqlDataSource dataSource) : ISourceArchive
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const string LiveScenes =
        """
        FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
        WHERE s.book_id = @bookId AND s.archived_at IS NULL AND c.archived_at IS NULL AND a.archived_at IS NULL
        """;

    public async Task<SourceArchiveResult> BuildAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        async Task<string> TextAsync(string sql) =>
            await connection.ExecuteScalarAsync<string>(new CommandDefinition(sql, new { bookId, formatVersion = BackupFormat.Version }, cancellationToken: cancellationToken)) ?? "null";

        async Task<JsonNode?> NodeAsync(string sql) => JsonNode.Parse(await TextAsync(sql));

        var book = (await NodeAsync(
            """
            SELECT jsonb_build_object(
                'formatVersion', @formatVersion::int, 'title', b.title, 'subtitle', b.subtitle, 'author', b.author_name, 'description', b.description,
                'status', b.status, 'targetWordCount', b.target_word_count, 'styleGuide', b.style_guide,
                'seriesName', s.title, 'seriesNumber', b.series_number, 'coverPalette', b.cover_palette,
                'citationStyle', (SELECT citation_style FROM book_publication_settings WHERE book_id = b.id),
                'publication', (SELECT to_jsonb(p) - 'book_id' - 'created_at' - 'updated_at' FROM publication_settings p WHERE p.book_id = b.id))::text
            FROM book b LEFT JOIN book s ON s.id = b.series_id WHERE b.id = @bookId
            """))!.AsObject();
        var acts = (await NodeAsync(
            """
            SELECT coalesce(jsonb_agg(to_jsonb(a) - 'book_id' - 'book_kind' - 'opener_asset_id' - 'opener_caption' - 'created_at' - 'updated_at' - 'archived_at'
                                      ORDER BY a.position, a.id), '[]')::text
            FROM act a WHERE a.book_id = @bookId AND a.archived_at IS NULL
            """))!.AsArray();
        var chapters = (await NodeAsync(
            """
            SELECT coalesce(jsonb_agg(to_jsonb(c) - 'book_id' - 'opener_asset_id' - 'opener_caption' - 'created_at' - 'updated_at' - 'archived_at'
                                      ORDER BY a.position, a.id, c.position, c.id), '[]')::text
            FROM chapter c JOIN act a ON a.id = c.act_id WHERE c.book_id = @bookId AND c.archived_at IS NULL AND a.archived_at IS NULL
            """))!.AsArray();
        var scenes = (await connection.QueryAsync<SceneRow>(new CommandDefinition(
            """
            SELECT s.id AS Id, s.chapter_id AS ChapterId, (to_jsonb(s) - 'book_id' - 'created_at' - 'updated_at' - 'archived_at')::text AS RowJson,
                   coalesce(p.body, '') AS Body, coalesce(p.word_count, 0) AS Words
            FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @bookId AND s.archived_at IS NULL AND c.archived_at IS NULL AND a.archived_at IS NULL
            ORDER BY a.position, a.id, c.position, c.id, s.position, s.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();

        var structure = new JsonObject
        {
            ["acts"] = acts,
            ["chapters"] = chapters,
            ["scenes"] = new JsonArray(),
            ["beats"] = await NodeAsync(
                $"""
                SELECT coalesce(jsonb_agg(to_jsonb(b) - 'created_at' - 'updated_at' - 'archived_at' ORDER BY b.scene_id, b.created_at, b.id), '[]')::text
                FROM beat b WHERE b.archived_at IS NULL AND b.scene_id IN (SELECT s.id {LiveScenes})
                """),
            ["scene_kb"] = await NodeAsync(
                $"""
                SELECT coalesce(jsonb_agg(to_jsonb(x) - 'archived_at'), '[]')::text FROM scene_kb x
                JOIN kb_entry e ON e.id = x.kb_entry_id AND e.book_id = @bookId AND e.archived_at IS NULL
                WHERE x.archived_at IS NULL AND x.scene_id IN (SELECT s.id {LiveScenes})
                """),
            ["beat_kb"] = await NodeAsync(
                $"""
                SELECT coalesce(jsonb_agg(to_jsonb(x) - 'archived_at'), '[]')::text FROM beat_kb x
                JOIN kb_entry e ON e.id = x.kb_entry_id AND e.book_id = @bookId AND e.archived_at IS NULL
                JOIN beat b ON b.id = x.beat_id AND b.archived_at IS NULL
                WHERE x.archived_at IS NULL AND b.scene_id IN (SELECT s.id {LiveScenes})
                """),
        };

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = AddScenes(zip, structure, acts, chapters, scenes);
            var knowledge = new JsonObject
            {
                ["categories"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(to_jsonb(k) - 'book_id' - 'created_at' - 'updated_at' - 'archived_at' ORDER BY k.position, k.id), '[]')::text
                    FROM kb_category k WHERE k.book_id = @bookId AND k.archived_at IS NULL
                    """),
                ["entries"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(to_jsonb(e) - 'book_id' - 'copied_from_id' - 'imported_at' - 'created_at' - 'updated_at' - 'archived_at'
                                              ORDER BY k.position, k.id, e.position, e.id), '[]')::text
                    FROM kb_entry e JOIN kb_category k ON k.id = e.category_id AND k.book_id = e.book_id
                    WHERE e.book_id = @bookId AND e.archived_at IS NULL
                    """),
                ["links"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(jsonb_build_object('entry_a_id', l.entry_a_id, 'entry_b_id', l.entry_b_id)), '[]')::text
                    FROM kb_entry_link l
                    JOIN kb_entry a ON a.id = l.entry_a_id AND a.book_id = @bookId AND a.archived_at IS NULL
                    JOIN kb_entry b ON b.id = l.entry_b_id AND b.book_id = @bookId AND b.archived_at IS NULL
                    WHERE l.archived_at IS NULL
                    """),
                ["additions"] = await NodeAsync(
                    $"""
                    SELECT coalesce(jsonb_agg(to_jsonb(p) - 'created_at' - 'updated_at' - 'archived_at' ORDER BY p.scene_id, p.created_at, p.id), '[]')::text
                    FROM kb_progression p JOIN kb_entry e ON e.id = p.kb_entry_id AND e.book_id = @bookId AND e.archived_at IS NULL
                    WHERE p.archived_at IS NULL AND p.scene_id IN (SELECT s.id {LiveScenes})
                    """),
            };
            var references = new JsonObject
            {
                ["sources"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(to_jsonb(r) - 'book_id' - 'book_kind' - 'created_by' - 'fair_use_decided_by' - 'signed_off_by' - 'merged_into_id'
                                              - 'updated_at' - 'archived_at' ORDER BY r.created_at, r.id), '[]')::text
                    FROM reference r WHERE r.book_id = @bookId AND r.archived_at IS NULL
                    """),
                ["uses"] = await NodeAsync(
                    $"""
                    SELECT coalesce(jsonb_agg(to_jsonb(u) - 'book_id' - 'asset_id' - 'generation_id' - 'word_count' - 'created_by' - 'updated_at' - 'archived_at'
                                              ORDER BY u.created_at, u.id), '[]')::text
                    FROM reference_use u JOIN reference r ON r.id = u.reference_id AND r.archived_at IS NULL
                    WHERE u.book_id = @bookId AND u.archived_at IS NULL AND u.generation_id IS NULL AND u.scene_id IN (SELECT s.id {LiveScenes})
                    """),
                ["permission_requests"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(to_jsonb(p) - 'created_by' - 'updated_at' ORDER BY p.created_at, p.id), '[]')::text
                    FROM permission_request p JOIN reference r ON r.id = p.reference_id AND r.book_id = @bookId AND r.archived_at IS NULL
                    """),
                ["quote_flags"] = await NodeAsync(
                    $"""
                    SELECT coalesce(jsonb_agg(to_jsonb(f) - 'book_id' - 'fingerprint' - 'resolved_by' ORDER BY f.created_at, f.id), '[]')::text
                    FROM quote_flag f WHERE f.book_id = @bookId AND f.scene_id IN (SELECT s.id {LiveScenes})
                    """),
            };
            var labels = new JsonObject
            {
                ["labels"] = await NodeAsync(
                    """
                    SELECT coalesce(jsonb_agg(to_jsonb(l) - 'book_id' - 'created_at' - 'updated_at' - 'archived_at' ORDER BY l.position, l.id), '[]')::text
                    FROM label l WHERE l.book_id = @bookId AND l.archived_at IS NULL
                    """),
                ["scene_labels"] = await NodeAsync(
                    $"""
                    SELECT coalesce(jsonb_agg(to_jsonb(x) - 'archived_at'), '[]')::text FROM scene_label x
                    JOIN label l ON l.id = x.label_id AND l.book_id = @bookId AND l.archived_at IS NULL
                    WHERE x.archived_at IS NULL AND x.scene_id IN (SELECT s.id {LiveScenes})
                    """),
            };

            Add(zip, "book.json", Pretty(book));
            Add(zip, "structure.json", Pretty(structure));
            Add(zip, "knowledge-base.json", Pretty(knowledge));
            Add(zip, "references.json", Pretty(references));
            Add(zip, "labels.json", Pretty(labels));
            Add(zip, "manifest.json", JsonSerializer.Serialize(
                new { formatVersion = BackupFormat.Version, title = book["title"]?.GetValue<string>(), author = book["author"]?.GetValue<string>(), scenes = files }, Json));
        }

        var notes = new List<string>();
        var drafts = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM ai_generation WHERE book_id = @bookId AND status IN ('streaming', 'pending')", new { bookId }, cancellationToken: cancellationToken));
        if (drafts > 0)
        {
            notes.Add($"{drafts} pending AI {(drafts == 1 ? "draft is" : "drafts are")} not included");
        }

        var figures = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = @bookId AND f.archived_at IS NULL", new { bookId }, cancellationToken: cancellationToken));
        notes.Add($"{figures} {(figures == 1 ? "image" : "images")} not included");
        return new SourceArchiveResult(stream.ToArray(), notes);
    }

    // Writes every scene's prose under its act and chapter folder and fills structure.scenes with each scene's row, `file` and `words`.
    private static List<string> AddScenes(ZipArchive zip, JsonObject structure, JsonArray acts, JsonArray chapters, List<SceneRow> scenes)
    {
        var actFolder = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (act, i) in acts.Select((a, i) => (a!.AsObject(), i)))
        {
            actFolder[act["id"]!.GetValue<string>()] = $"manuscript/act-{i + 1:00}-{Slug(act["title"]?.GetValue<string>(), "act")}";
        }

        var chapterFolder = new Dictionary<string, string>(StringComparer.Ordinal);
        var inAct = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chapter in chapters.Select(c => c!.AsObject()))
        {
            var act = chapter["act_id"]!.GetValue<string>();
            var n = inAct[act] = inAct.GetValueOrDefault(act) + 1;
            chapterFolder[chapter["id"]!.GetValue<string>()] = $"{actFolder[act]}/chapter-{n:00}-{Slug(chapter["title"]?.GetValue<string>(), "chapter")}";
        }

        var inChapter = new Dictionary<Guid, int>();
        var files = new List<string>();
        var rows = (JsonArray)structure["scenes"]!;
        foreach (var scene in scenes)
        {
            var n = inChapter[scene.ChapterId] = inChapter.GetValueOrDefault(scene.ChapterId) + 1;
            var path = $"{chapterFolder[scene.ChapterId.ToString()]}/scene-{n:00}.md";
            files.Add(path);
            var row = JsonNode.Parse(scene.RowJson)!.AsObject();
            row["file"] = path;
            row["words"] = scene.Words;
            rows.Add(row);
            Add(zip, path, scene.Body.Length == 0 || scene.Body.EndsWith('\n') ? scene.Body : scene.Body + "\n");
        }

        return files;
    }

    private static string Pretty(JsonNode? node) => node?.ToJsonString(Json) ?? "null";

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Slug(string? title, string fallback)
    {
        var slug = new StringBuilder();
        foreach (var c in (title ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        if (text.Length == 0)
        {
            return fallback;
        }

        return text.Length > 48 ? text[..48].TrimEnd('-') : text;
    }

    private sealed record SceneRow(Guid Id, Guid ChapterId, string RowJson, string Body, int Words);
}
