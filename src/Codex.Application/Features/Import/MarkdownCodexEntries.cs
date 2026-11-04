namespace Codex.Application.Features.Import;

// The knowledge base entries a Markdown folder's `codex:` front matter names, scene by scene and chapter by chapter.
// An item is a name ("Mara"), or a mapping with a name and a `path` to the entry's file and a `url` it came from:
//   codex:
//     - name: Goodhart's Law
//       path: ../other/goodharts-law/entry.md
//       url: https://example.com/goodhart
// A path is looked up among the uploaded files only (never on disk): relative to the referencing file, then to the
// manuscript folder, then to the folder that holds it. One that resolves keys the entry by that path (case-insensitive),
// so two files with one name are two entries, and the file's text becomes the body; its tags/type (as the Novelcrafter
// zip names them) pick the category, with aliases and always-in-context kept. Anything else keys by lower-cased name
// and is a name-only entry in "Imported", as before. Entries sharing a name in one category are told apart by the
// chapter that first names them (Finish).
internal sealed class MarkdownCodexEntries(
    IReadOnlyList<ImportFile> files, string root, string parent, ImportEntryList list, List<ImportSkippedItem> skipped)
{
    public const string PathKeyPrefix = "path:";

    private readonly Dictionary<string, ImportFile> _files = Index(files);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _chapters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _byName = new(StringComparer.Ordinal);

    // Adds what front matter's codex list names; returns the keys of the entries that are in the list now (what a scene
    // links to). A chapter's references are added the same way and its caller drops the keys.
    public IReadOnlyList<string> Add(FrontMatter front, string referencingPath, string chapterTitle)
    {
        var keys = new List<string>();
        var refs = Ordered(front);
        foreach (var reference in refs)
        {
            if (AddOne(reference, referencingPath, chapterTitle) is { } key && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }
        return keys;
    }

    // The list's items as written: plain names and mappings in their original order.
    private static List<CodexRef> Ordered(FrontMatter front)
    {
        var plain = front.List("codex").Select(n => new CodexRef(n.Trim(), null, null)).ToList();
        var refs = new List<CodexRef>();
        var next = 0;
        foreach (var item in front.Objects("codex"))
        {
            var before = int.TryParse(item.GetValueOrDefault(FrontMatter.PlainBefore), out var n) ? Math.Min(n, plain.Count) : plain.Count;
            refs.AddRange(plain.Skip(next).Take(before - next));
            next = Math.Max(next, before);
            refs.Add(new CodexRef(
                item.GetValueOrDefault("name")?.Trim() ?? item.GetValueOrDefault("title")?.Trim(), item.GetValueOrDefault("path")?.Trim(), item.GetValueOrDefault("url")?.Trim()));
        }
        refs.AddRange(plain.Skip(next));
        return refs;
    }

    // Files an entry took its text from: not "skipped" any more.
    public bool Used(string path) => _used.Contains(path);

    // Names told apart, and the one note about paths that matched no uploaded file.
    public void Finish(string where)
    {
        foreach (var group in list.Items.GroupBy(e => (e.Category.ToLowerInvariant(), e.Name.ToLowerInvariant())).Where(g => g.Count() > 1).ToList())
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                var chapter = _chapters.GetValueOrDefault(entry.Key);
                var name = chapter == null ? entry.Name : MarkdownFolderParser.Clip($"{entry.Name} ({chapter})");
                var unique = name;
                var n = 2;
                while (!taken.Add(unique))
                {
                    unique = MarkdownFolderParser.Clip($"{name} ({n++})");
                }
                list.Replace(entry with { Name = unique });
            }
        }
        if (_missing.Count > 0)
        {
            skipped.Add(new ImportSkippedItem(where,
                $"{_missing.Count} codex files named by path are not in the upload (pick the folder that holds them too); those entries have a name and no text"));
        }
    }

    private string? AddOne(CodexRef reference, string referencingPath, string chapterTitle)
    {
        var file = reference.Path == null ? null : Find(reference.Path, referencingPath);
        if (reference.Path != null && file == null)
        {
            _missing.Add(reference.Path);
        }
        if (file != null && ReadEntry(file, reference) is { } key)
        {
            return Track(key, chapterTitle);
        }
        return AddByName(reference, chapterTitle);
    }

    private string? ReadEntry(ImportFile file, CodexRef reference)
    {
        var key = PathKeyPrefix + file.Path.ToLowerInvariant();
        if (_chapters.ContainsKey(key))
        {
            return key;
        }
        if (file.Content.Length > ImportLimits.MaxFileBytes)
        {
            skipped.Add(new ImportSkippedItem(file.Path, ImportLimits.TooLarge));
            return null;
        }
        if (NovelcrafterCodex.ReadFile(file, skipped) is not { } read)
        {
            return null;
        }
        var id = read.Id != null && _ids.Add(read.Id) ? read.Id : null;
        var name = MarkdownFolderParser.Clip(string.IsNullOrWhiteSpace(reference.Name) ? read.Name : reference.Name);
        var category = Category(NovelcrafterCodex.CategoryFromTags(read) ?? MarkdownFolderParser.ImportedCategory);
        var aliases = read.Aliases.Where(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!list.Add(new ImportEntryPlan(key, category, name, id, read.Summary, read.Body, aliases, ImportUrl: NullIfEmpty(reference.Url), AlwaysInContext: read.AlwaysInContext)))
        {
            return null;
        }
        _used.Add(file.Path);
        _byName[name.ToLowerInvariant()] = _byName.ContainsKey(name.ToLowerInvariant()) ? null : key;
        return key;
    }

    private string? AddByName(CodexRef reference, string chapterTitle)
    {
        var name = reference.Name;
        if (string.IsNullOrWhiteSpace(name) && reference.Path != null)
        {
            name = MarkdownFolderParser.Humanize(StemOf(reference.Path));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var lower = name.ToLowerInvariant();
        // A plain name for something a path already brought in, if there is exactly one such entry.
        if (reference.Path == null && _byName.TryGetValue(lower, out var existing) && existing != null)
        {
            return Track(existing, chapterTitle);
        }
        if (!list.Add(new ImportEntryPlan(lower, MarkdownFolderParser.ImportedCategory, MarkdownFolderParser.Clip(name), null, null, null, ImportUrl: NullIfEmpty(reference.Url))))
        {
            return null;
        }
        return Track(lower, chapterTitle);
    }

    private string Track(string key, string chapterTitle)
    {
        _chapters.TryAdd(key, chapterTitle);
        return key;
    }

    // A category spelled one way however its tags capitalise it ("Laws" and "laws" are one category).
    private string Category(string name) => _categories.TryAdd(name, name) ? name : _categories[name];

    // Relative to the referencing file, then the manuscript folder, then its parent. Only files in the upload can match;
    // a path that climbs out of the upload, or is not a plain path, matches nothing.
    private ImportFile? Find(string path, string referencingPath)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || relative.Contains(':') || relative.Any(char.IsControl))
        {
            return null;
        }
        foreach (var baseDir in new[] { ParentOf(referencingPath), root, parent }.Distinct(StringComparer.Ordinal))
        {
            if (Normalize(baseDir, relative) is { } resolved && _files.TryGetValue(resolved, out var file))
            {
                return file;
            }
        }
        return null;
    }

    private static string? Normalize(string baseDir, string relative)
    {
        var parts = new List<string>(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }
                parts.RemoveAt(parts.Count - 1);
            }
            else if (segment != ".")
            {
                parts.Add(segment);
            }
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static Dictionary<string, ImportFile> Index(IReadOnlyList<ImportFile> files)
    {
        var index = new Dictionary<string, ImportFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            index.TryAdd(file.Path, file);
        }
        return index;
    }

    private static string StemOf(string path)
    {
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    private static string ParentOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private sealed record CodexRef(string? Name, string? Path, string? Url);
}
