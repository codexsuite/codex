using System.IO.Compression;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// A Novelcrafter export: one .zip holding the manuscript in the Markdown layout of issue #10 (acts, chapters, scenes,
// CLAUDE.md and MEMORY.md; the Markdown folder importer reads it) and the Codex as entry files in kind folders
// (NovelcrafterCodex). Codex entries become the book's knowledge base with the KSUID in external_id, and a scene's
// codex[] names an entry by KSUID, name or alias. Names that match no entry are imported as entries in "Imported", as
// the Markdown folder does; a KSUID that matches none is listed as skipped. Registered in ImportServices.
public sealed class NovelcrafterZipParser : IImportParser
{
    private const string What = "Novelcrafter export (.zip)";
    private const string KeyPrefix = "nc:";

    private readonly MarkdownFolderParser _layout = new();

    public string Source => "novelcrafter";

    public ImportPlan Parse(ImportUpload upload)
    {
        var zip = ImportArchive.Single(upload, ".zip", What);
        var skipped = new List<ImportSkippedItem>(upload.Omitted);
        var (files, fileCount) = Unzip(zip, skipped);
        var codexFiles = files.Where(f => NovelcrafterCodex.IsCodexPath(f.Path)).ToList();
        var manuscript = files.Except(codexFiles).ToList();
        if (!manuscript.Any(f => f.Path.Split('/').Any(IsActFolder)))
        {
            throw ApiException.Validation("This zip does not look like a Novelcrafter export: no manuscript with act-NN folders was found.");
        }

        var codex = NovelcrafterCodex.Read(codexFiles, skipped);
        var plan = _layout.Parse(new ImportUpload("markdown", manuscript, skipped));
        return Link(plan with { FileName = zip.Path, FileCount = fileCount + upload.Omitted.Count }, codex, SceneCodexSpellings(manuscript));
    }

    // The Markdown files, read; everything else is listed as skipped without being read.
    private static (List<ImportFile> Files, int Count) Unzip(ImportFile zip, List<ImportSkippedItem> skipped)
    {
        using var archive = ImportArchive.Open(zip.Content, What);
        var entries = ImportArchive.Files(archive, What);
        var markdown = new List<ZipArchiveEntry>();
        foreach (var entry in entries)
        {
            if (!entry.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new ImportSkippedItem(entry.FullName, ImportLimits.NotMarkdown));
            }
            else if (entry.Length > ImportLimits.MaxFileBytes)
            {
                skipped.Add(new ImportSkippedItem(entry.FullName, ImportLimits.TooLarge));
            }
            else
            {
                markdown.Add(entry);
            }
        }
        ImportArchive.CheckExpansion(markdown, What);
        var files = markdown.Select(e => new ImportFile(e.FullName, ImportArchive.Read(e, ImportLimits.MaxFileBytes, What))).ToList();
        return (files, entries.Count);
    }

    // Replaces the Markdown parser's name-only entries with the Codex's, and points each scene at them.
    private static ImportPlan Link(ImportPlan plan, IReadOnlyList<NovelcrafterCodexFile> codex, Dictionary<string, string> spellings)
    {
        var skipped = plan.Skipped.ToList();
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var real = codex.Select((c, i) => (File: c, Key: KeyPrefix + i)).ToList();
        foreach (var (file, key) in real.Where(r => r.File.Id != null))
        {
            keys.TryAdd(file.Id!, key);
        }
        foreach (var (file, key) in real)
        {
            keys.TryAdd(file.Name, key);
        }
        foreach (var (file, key) in real)
        {
            foreach (var alias in file.Aliases)
            {
                keys.TryAdd(alias, key);
            }
        }

        var unknownIds = new HashSet<string>(StringComparer.Ordinal);
        string? Resolve(string key)
        {
            if (keys.TryGetValue(key, out var found))
            {
                return found;
            }
            if (NovelcrafterCodex.LooksLikeKsuid(key))
            {
                unknownIds.Add(key);
                return null;
            }
            return key;
        }

        var acts = plan.Acts.Select(a => a with
        {
            Chapters = a.Chapters.Select(c => c with
            {
                Scenes = c.Scenes.Select(s => s with { Entries = s.Entries.Select(Resolve).OfType<string>().Distinct().ToList() }).ToList(),
            }).ToList(),
        }).ToList();
        foreach (var id in unknownIds.Order(StringComparer.Ordinal))
        {
            skipped.Add(new ImportSkippedItem("Scene codex links", $"Codex id \"{spellings.GetValueOrDefault(id, id)}\" is not in the export; the link was left out"));
        }

        var entries = new ImportEntryList();
        foreach (var (file, key) in real)
        {
            entries.Add(new ImportEntryPlan(key, file.Category, file.Name, file.Id, file.Summary, file.Body, file.Aliases));
        }
        var kept = acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).SelectMany(s => s.Entries).ToHashSet(StringComparer.Ordinal);
        foreach (var imported in plan.Entries.Where(e => kept.Contains(e.Key) || e.Key.StartsWith(MarkdownCodexEntries.PathKeyPrefix, StringComparison.Ordinal)))
        {
            entries.Add(imported);
        }
        if (entries.Dropped > 0)
        {
            skipped.Add(new ImportSkippedItem(plan.FileName,
                $"Only the first {ImportLimits.MaxEntries} knowledge base entries are imported; {entries.Dropped} more names were left out"));
        }
        return plan with { Acts = acts, Entries = entries.Items, Skipped = skipped };
    }

    // The Markdown parser keys scene links in lower case; this gives the text as the scene wrote it, for messages.
    private static Dictionary<string, string> SceneCodexSpellings(IReadOnlyList<ImportFile> manuscript)
    {
        var spellings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in manuscript.Where(f => f.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            var text = System.Text.Encoding.UTF8.GetString(file.Content).Replace("\r\n", "\n");
            foreach (var name in FrontMatter.Split(text).FrontMatter.List("codex").Select(n => n.Trim()))
            {
                spellings.TryAdd(name.ToLowerInvariant(), name);
            }
        }
        return spellings;
    }

    private static bool IsActFolder(string segment) =>
        segment.StartsWith("act-", StringComparison.Ordinal) && segment.Length > 4 && char.IsAsciiDigit(segment[4]);
}
