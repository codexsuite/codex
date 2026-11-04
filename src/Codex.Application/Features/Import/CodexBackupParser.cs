using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Errors;
using Codex.Contracts.Import;

namespace Codex.Application.Features.Import;

// A Codex backup: the .zip the source export writes (SourceArchive documents its layout). It is read into an ImportPlan
// with the counts the preview shows; BackupWriter creates the new book from the BackupData the plan carries.
// 400 "This is not a Codex backup" when book.json is missing or unreadable, "This backup was made by a newer Codex"
// when its format version is higher than this one reads. Registered in ImportServices.
public sealed class CodexBackupParser : IImportParser
{
    private const string What = "Codex backup (.zip)";
    private const string NotABackup = "This is not a Codex backup";

    public string Source => "codex_zip";

    public ImportPlan Parse(ImportUpload upload)
    {
        var zip = ImportArchive.Single(upload, ".zip", What);
        using var archive = ImportArchive.Open(zip.Content, What);
        var entries = ImportArchive.Files(archive, What).ToDictionary(e => e.FullName, StringComparer.Ordinal);
        if (!entries.TryGetValue("book.json", out var bookEntry))
        {
            throw ApiException.Validation($"{NotABackup}: it has no book.json.");
        }

        ImportArchive.CheckExpansion(entries.Values, What);
        var book = ReadObject(bookEntry) ?? throw ApiException.Validation($"{NotABackup}: book.json is not a backup's book file.");
        if (book["formatVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number < 1)
        {
            throw ApiException.Validation($"{NotABackup}: book.json has no format version.");
        }

        if (number > BackupFormat.Version)
        {
            throw ApiException.Validation("This backup was made by a newer Codex. Update Codex to restore it.");
        }

        var title = Text(book, "title");
        if (title is null || title.Trim().Length == 0)
        {
            throw ApiException.Validation($"{NotABackup}: the book has no title.");
        }

        var structure = Optional(entries, "structure.json");
        var knowledge = Optional(entries, "knowledge-base.json");
        var references = Optional(entries, "references.json");
        var labels = Optional(entries, "labels.json");
        foreach (var file in new[] { structure, knowledge, references, labels })
        {
            BackupDates.CheckFile(file);
        }

        BackupDates.Check(book, "book");
        BackupDates.CheckFile(book);
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var words = 0;
        foreach (var scene in Rows(structure, "scenes"))
        {
            var file = Text(scene, "file") ?? throw ApiException.Validation("The backup lists a scene without a file.");
            if (!entries.TryGetValue(file, out var entry) || !file.EndsWith(".md", StringComparison.Ordinal))
            {
                throw ApiException.Validation($"The backup lists \"{file}\", which is not in the zip.");
            }

            bodies[file] = System.Text.Encoding.UTF8.GetString(ImportArchive.Read(entry, ImportLimits.MaxTotalBytes, What));
            words += scene["words"] is JsonValue w && w.TryGetValue<int>(out var count) ? count : 0;
        }

        var data = new BackupData(
            book, structure, knowledge, references, labels, bodies, words,
            Rows(references, "sources").Count(),
            Rows(references, "uses").Select(u => Text(u, "reference_id")).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Rows(labels, "labels").Count());
        return new ImportPlan(
            zip.Path, entries.Count,
            new ImportBookPlan(title.Trim(), Text(book, "subtitle"), Text(book, "author"), Text(book, "description"), Text(book, "styleGuide")),
            Acts(structure, bodies), Entries(knowledge), upload.Omitted, Backup: data);
    }

    private static List<ImportActPlan> Acts(JsonObject structure, Dictionary<string, string> bodies)
    {
        var beats = Rows(structure, "beats").ToLookup(b => Text(b, "scene_id") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var scenes = Rows(structure, "scenes").ToLookup(s => Text(s, "chapter_id") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var chapters = Rows(structure, "chapters").ToLookup(c => Text(c, "act_id") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        return Rows(structure, "acts").Select(act => new ImportActPlan(
            Text(act, "title") ?? string.Empty,
            Text(act, "summary"),
            chapters[Text(act, "id") ?? string.Empty].Select(chapter => new ImportChapterPlan(
                Text(chapter, "title") ?? string.Empty,
                Text(chapter, "summary"),
                Text(chapter, "notes"),
                chapter["is_numbered"] is not JsonValue numbered || !numbered.TryGetValue<bool>(out var flag) || flag,
                scenes[Text(chapter, "id") ?? string.Empty].Select(scene => new ImportScenePlan(
                    Text(scene, "title"),
                    Text(scene, "summary"),
                    Text(scene, "plan_notes"),
                    Text(scene, "status") ?? "outline",
                    Text(scene, "import_ref"),
                    false,
                    beats[Text(scene, "id") ?? string.Empty].Select(b => Text(b, "body") ?? string.Empty).ToList(),
                    bodies[Text(scene, "file")!] is { Length: > 0 } body ? body : null,
                    [])).ToList())).ToList())).ToList();
    }

    private static List<ImportEntryPlan> Entries(JsonObject knowledge)
    {
        var categories = Rows(knowledge, "categories").ToDictionary(c => Text(c, "id") ?? string.Empty, c => Text(c, "name") ?? "Imported", StringComparer.OrdinalIgnoreCase);
        return Rows(knowledge, "entries").Select(e => new ImportEntryPlan(
            Text(e, "id") ?? string.Empty,
            categories.GetValueOrDefault(Text(e, "category_id") ?? string.Empty, "Imported"),
            Text(e, "name") ?? string.Empty,
            Text(e, "external_id"),
            Text(e, "summary"),
            Text(e, "body"))).ToList();
    }

    private static JsonObject Optional(Dictionary<string, ZipArchiveEntry> entries, string name) =>
        entries.TryGetValue(name, out var entry) ? ReadObject(entry) ?? throw ApiException.Validation($"{NotABackup}: {name} is not readable.") : new JsonObject();

    private static JsonObject? ReadObject(ZipArchiveEntry entry)
    {
        JsonObject? read = null;
        try
        {
            read = JsonNode.Parse(ImportArchive.Read(entry, ImportLimits.MaxTotalBytes, What)) as JsonObject;
        }
        catch (JsonException)
        {
            // not JSON: the caller says so
        }

        return read;
    }

    // The object rows of a table-shaped array property; absent is none.
    private static IEnumerable<JsonObject> Rows(JsonObject file, string property) =>
        file[property] is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string? Text(JsonObject row, string property) =>
        row[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
