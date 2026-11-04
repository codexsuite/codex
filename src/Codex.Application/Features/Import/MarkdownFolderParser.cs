using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Import;

// The Markdown-folder layout of issue #10's import mapping:
//   index.md (book), CLAUDE.md and MEMORY.md (style guide, here or one folder up),
//   act-NN-*/index.md, act-NN-*/chapter-NN-*/index.md, …/scene-NN.md and …/scene-NN-prose.md.
// The folder may be the manuscript itself or any folder above it: the book root is the shallowest folder holding
// act-NN folders. Everything else is listed as skipped with a reason. Registered in ImportServices.
public sealed partial class MarkdownFolderParser : IImportParser
{
    public const string ImportedCategory = "Imported";

    private const string BeatsHeading = "Scene Beats";
    private const string OpenQuestionsHeading = "Open questions for the author";
    private const string ShapeHeading = "Shape of the chapter";

    private static readonly string[] Statuses = ["empty", "outline", "beats", "draft", "revised", "final"];

    public string Source => "markdown";

    public ImportPlan Parse(ImportUpload upload)
    {
        var files = upload.Files;
        if (!files.Any(f => IsMarkdown(f.Path)))
        {
            throw ApiException.Validation("The folder has no Markdown (.md) files, so there is nothing to import.");
        }
        var root = FindRoot(files)
            ?? throw ApiException.Validation("This folder does not have the Markdown layout: no act-NN folders were found.");
        var skipped = new List<ImportSkippedItem>(upload.Omitted);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var parent = ParentOf(root);
        foreach (var file in files)
        {
            Classify(file, root, parent, skipped, texts);
        }

        var book = ParseBook(root, parent, texts);
        var entries = new ImportEntryList();
        var codex = new MarkdownCodexEntries(files, root, parent, entries, skipped);
        var acts = ParseActs(root, texts, codex, skipped);
        codex.Finish(FolderName(files, root));
        skipped.RemoveAll(s => codex.Used(s.Path));
        if (entries.Dropped > 0)
        {
            skipped.Add(new ImportSkippedItem(FolderName(files, root),
                $"Only the first {ImportLimits.MaxEntries} knowledge base entries are imported; {entries.Dropped} more names were left out"));
        }
        return new ImportPlan(FolderName(files, root), files.Count + upload.Omitted.Count, book, acts, entries.Items, skipped);
    }

    private static void Classify(ImportFile file, string root, string parent, List<ImportSkippedItem> skipped, Dictionary<string, string> texts)
    {
        var path = file.Path;
        var inRoot = root.Length == 0 || path.StartsWith(root + "/", StringComparison.Ordinal);
        var relative = root.Length == 0 || !inRoot ? path : path[(root.Length + 1)..];
        var styleFile = IsStyleFile(path, root) || IsStyleFile(path, parent);
        if (!inRoot && !styleFile)
        {
            skipped.Add(new ImportSkippedItem(path, "Outside the manuscript folder"));
        }
        else if (!IsMarkdown(path))
        {
            skipped.Add(new ImportSkippedItem(path, ImportLimits.NotMarkdown));
        }
        else if (!styleFile && !IsLayoutFile(relative))
        {
            skipped.Add(new ImportSkippedItem(path, "Not part of the import layout"));
        }
        else if (file.Content.Length > ImportLimits.MaxFileBytes)
        {
            skipped.Add(new ImportSkippedItem(path, ImportLimits.TooLarge));
        }
        else if (Decode(file.Content) is { } text)
        {
            texts[path] = text;
        }
        else
        {
            skipped.Add(new ImportSkippedItem(path, "Not UTF-8 text"));
        }
    }

    private static ImportBookPlan ParseBook(string root, string parent, Dictionary<string, string> texts)
    {
        var (front, body) = texts.TryGetValue(Join(root, "index.md"), out var index) ? FrontMatter.Split(index) : (FrontMatter.Empty, "");
        var (heading, rest) = TakeHeading(body);
        var title = front.Scalar("title") ?? heading ?? Humanize(LastSegment(root)) ?? "Imported book";
        var guide = new[] { "CLAUDE.md", "MEMORY.md" }
            .Select(name => texts.GetValueOrDefault(Join(root, name)) ?? (root.Length > 0 ? texts.GetValueOrDefault(Join(parent, name)) : null))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => FrontMatter.Split(t!).Body.Trim())
            .ToList();
        return new ImportBookPlan(
            Clip(title),
            front.Scalar("subtitle"),
            front.Scalar("author"),
            NullIfEmpty(rest),
            guide.Count == 0 ? null : string.Join("\n\n", guide));
    }

    private static List<ImportActPlan> ParseActs(string root, Dictionary<string, string> texts, MarkdownCodexEntries codex, List<ImportSkippedItem> skipped)
    {
        var layout = texts.Keys
            .Where(p => root.Length == 0 || p.StartsWith(root + "/", StringComparison.Ordinal))
            .Select(p => (Path: p, Parts: (root.Length == 0 ? p : p[(root.Length + 1)..]).Split('/')))
            .Where(f => f.Parts.Length >= 2)
            .ToList();
        var importRefs = new HashSet<string>(StringComparer.Ordinal);
        var acts = new List<(int? Order, int Number, string Dir, ImportActPlan Act)>();
        foreach (var actDir in layout.Select(f => f.Parts[0]).Distinct().Order(StringComparer.Ordinal))
        {
            var (front, body) = Read(texts, Join(root, actDir, "index.md"));
            var (heading, rest) = TakeHeading(body);
            var chapters = layout
                .Where(f => f.Parts[0] == actDir && f.Parts.Length >= 3)
                .Select(f => f.Parts[1])
                .Distinct()
                .OrderBy(dir => Number(ChapterDir(), dir))
                .ThenBy(dir => dir, StringComparer.Ordinal)
                .Select(dir => ParseChapter(texts, Join(root, actDir, dir), dir, layout, codex, importRefs, skipped))
                .ToList();
            var act = new ImportActPlan(
                Clip(front.Scalar("title") ?? heading ?? Humanize(actDir) ?? actDir),
                front.Scalar("summary") ?? NullIfEmpty(rest),
                chapters);
            acts.Add((front.Number("reading_order"), Number(ActDir(), actDir), actDir, act));
        }
        return acts.OrderBy(a => a.Order ?? a.Number).ThenBy(a => a.Number).ThenBy(a => a.Dir, StringComparer.Ordinal).Select(a => a.Act).ToList();
    }

    private static ImportChapterPlan ParseChapter(
        Dictionary<string, string> texts,
        string dir,
        string name,
        List<(string Path, string[] Parts)> layout,
        MarkdownCodexEntries codex,
        HashSet<string> importRefs,
        List<ImportSkippedItem> skipped)
    {
        var (front, body) = Read(texts, Join(dir, "index.md"));
        var (heading, rest) = TakeHeading(body);
        var sections = MarkdownSections.Split(rest);
        var shape = sections.Sections.FirstOrDefault(s => Is(s.Heading, ShapeHeading));
        var title = Clip(front.Scalar("title") ?? heading ?? Humanize(name) ?? name);
        // A chapter's codex list brings its entries in; nothing links them (no chapter-to-entry table), so only scenes' own lists do.
        codex.Add(front, Join(dir, "index.md"), title);
        var notes = new[] { sections.Lead }
            .Concat(sections.Sections.Where(s => s != shape).Select(s => s.ToMarkdown()))
            .Where(t => t.Length > 0);

        var sceneFiles = layout.Where(f => f.Path.StartsWith(dir + "/", StringComparison.Ordinal) && f.Path.Count(c => c == '/') == dir.Count(c => c == '/') + 1)
            .Select(f => f.Path)
            .ToList();
        var scenes = sceneFiles
            .Where(p => SceneFile().IsMatch(LastSegment(p)))
            .OrderBy(p => Number(SceneFile(), LastSegment(p)))
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => ParseScene(texts, p, codex, title, importRefs, skipped))
            .ToList();
        foreach (var prose in sceneFiles.Where(p => ProseFile().IsMatch(LastSegment(p))))
        {
            var scenePath = Join(dir, $"scene-{ProseFile().Match(LastSegment(prose)).Groups[1].Value}.md");
            if (!texts.ContainsKey(scenePath))
            {
                skipped.Add(new ImportSkippedItem(prose, "No scene file for this prose"));
            }
        }

        return new ImportChapterPlan(
            title,
            front.Scalar("summary") ?? NullIfEmpty(shape?.Text),
            NullIfEmpty(string.Join("\n\n", notes)),
            !front.Flag("front_matter"),
            scenes);
    }

    private static ImportScenePlan ParseScene(
        Dictionary<string, string> texts,
        string path,
        MarkdownCodexEntries codex,
        string chapterTitle,
        HashSet<string> importRefs,
        List<ImportSkippedItem> skipped)
    {
        var (front, body) = FrontMatter.Split(texts[path]);
        var sections = MarkdownSections.Split(body);
        var beatsSection = sections.Sections.FirstOrDefault(s => Is(s.Heading, BeatsHeading));
        var notes = new[] { sections.Lead }
            .Concat(sections.Sections.Where(s => s != beatsSection && !Is(s.Heading, OpenQuestionsHeading)).Select(s => s.ToMarkdown()))
            .Where(t => t.Length > 0);
        var beats = beatsSection == null ? [] : Beats(beatsSection.Text);

        var prosePath = path[..^".md".Length] + "-prose.md";
        var prose = texts.TryGetValue(prosePath, out var proseText) ? MapProse(prosePath, FrontMatter.Split(proseText).Body, skipped) : "";

        var status = front.Scalar("status")?.ToLowerInvariant();
        var archived = status == "folded" || string.Equals(front.Scalar("scene"), "removed", StringComparison.OrdinalIgnoreCase);
        if (!Statuses.Contains(status))
        {
            if (status != null && status != "folded")
            {
                skipped.Add(new ImportSkippedItem(path, $"Status \"{front.Scalar("status")}\" is not a Codex status; set from the scene's content"));
            }
            status = DerivedStatus(prose, beats);
        }

        var importRef = front.Scalar("id");
        if (importRef != null && !importRefs.Add(importRef))
        {
            skipped.Add(new ImportSkippedItem(path, $"Scene id \"{importRef}\" is used twice; kept on the first scene only"));
            importRef = null;
        }

        var links = codex.Add(front, path, chapterTitle);

        return new ImportScenePlan(
            front.Scalar("title") is { } title ? Clip(title) : null,
            front.Scalar("summary"),
            NullIfEmpty(string.Join("\n\n", notes)),
            status!,
            importRef,
            archived,
            beats,
            prose.Length == 0 ? null : prose,
            links);
    }

    // The scene's prose with its LaTeX mapped (or kept as raw LaTeX, which is listed). A spell header has no entry to point
    // at here (the import builds no spell entries), so it is kept as raw LaTeX too.
    private static string MapProse(string path, string body, List<ImportSkippedItem> skipped)
    {
        var mapped = LatexImportMapper.Map(body);
        if (mapped.Fenced.Count > 0)
        {
            var blocks = mapped.Fenced.Count(f => f.Kind == "block");
            var first = mapped.Fenced[0];
            skipped.Add(new ImportSkippedItem(path,
                $"{blocks} LaTeX blocks and {mapped.Fenced.Count - blocks} inline commands were kept as raw LaTeX (first: \\{first.Command}, line {first.Line})"));
        }
        return mapped.Prose;
    }

    // Numbered (or "-") items; a wrapped line continues its item, a blank line inside one keeps a paragraph break.
    private static List<string> Beats(string text)
    {
        var beats = new List<StringBuilder>();
        var blank = false;
        foreach (var line in text.Split('\n'))
        {
            var item = BeatItem().Match(line);
            if (item.Success)
            {
                beats.Add(new StringBuilder(item.Groups[1].Value.Trim()));
                blank = false;
            }
            else if (line.Trim().Length == 0)
            {
                blank = true;
            }
            else if (beats.Count > 0)
            {
                beats[^1].Append(blank ? "\n\n" : " ").Append(line.Trim());
                blank = false;
            }
        }
        return beats.Select(b => b.ToString()).Where(b => b.Length > 0).ToList();
    }

    // The shallowest folder with an act-NN child that holds files; "" when the acts sit at the top of the upload.
    private static string? FindRoot(IReadOnlyList<ImportFile> files)
    {
        string? best = null;
        foreach (var parts in files.Select(f => f.Path.Split('/')))
        {
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!ActDir().IsMatch(parts[i]))
                {
                    continue;
                }
                var candidate = string.Join('/', parts[..i]);
                if (best == null || candidate.Count(c => c == '/') < best.Count(c => c == '/') || (candidate.Length == 0 && best.Length > 0))
                {
                    best = candidate;
                }
                break;
            }
        }
        return best;
    }

    private static bool IsLayoutFile(string relative)
    {
        var parts = relative.Split('/');
        return parts switch
        {
            ["index.md" or "CLAUDE.md" or "MEMORY.md"] => true,
            [var act, "index.md"] => ActDir().IsMatch(act),
            [var act, var chapter, var file] => ActDir().IsMatch(act) && ChapterDir().IsMatch(chapter)
                && (file == "index.md" || SceneFile().IsMatch(file) || ProseFile().IsMatch(file)),
            _ => false,
        };
    }

    private static bool IsStyleFile(string path, string folder) =>
        path == Join(folder, "CLAUDE.md") || path == Join(folder, "MEMORY.md");

    private static string FolderName(IReadOnlyList<ImportFile> files, string root)
    {
        var first = files[0].Path.Split('/')[0];
        if (files.All(f => f.Path.StartsWith(first + "/", StringComparison.Ordinal)))
        {
            return first;
        }
        return root.Length > 0 ? LastSegment(root) : "Markdown folder";
    }

    private static string DerivedStatus(string prose, List<string> beats)
    {
        if (prose.Length > 0)
        {
            return "draft";
        }
        return beats.Count > 0 ? "beats" : "outline";
    }

    private static (FrontMatter Front, string Body) Read(Dictionary<string, string> texts, string path) =>
        texts.TryGetValue(path, out var text) ? FrontMatter.Split(text) : (FrontMatter.Empty, "");

    // A leading "# Title" line and the rest.
    private static (string? Heading, string Remainder) TakeHeading(string body)
    {
        var trimmed = body.TrimStart('\n', ' ');
        if (!trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            return (null, body.Trim('\n', ' '));
        }
        var end = trimmed.IndexOf('\n');
        return end < 0 ? (trimmed[2..].Trim(), "") : (trimmed[2..end].Trim(), trimmed[(end + 1)..].Trim('\n', ' '));
    }

    private static string? Decode(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(content);
            return text.TrimStart('\uFEFF').Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // "act-01-the-call" -> "The call".
    public static string? Humanize(string? dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }
        var words = LayoutPrefix().Replace(dir, "").Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? null : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    private static int Number(Regex pattern, string name) =>
        int.TryParse(pattern.Match(name).Groups[1].Value, out var n) ? n : int.MaxValue;

    private static bool IsMarkdown(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool Is(string heading, string expected) => string.Equals(heading.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // At most 200 UTF-16 units, never cutting a surrogate pair.
    public static string Clip(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= 200)
        {
            return trimmed;
        }
        var end = char.IsHighSurrogate(trimmed[199]) ? 199 : 200;
        return trimmed[..end].Trim();
    }

    private static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string ParentOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    private static string Join(params string[] parts) => string.Join('/', parts.Where(p => p.Length > 0));

    [GeneratedRegex(@"^act-(\d+)(?:-.*)?$")]
    private static partial Regex ActDir();

    [GeneratedRegex(@"^chapter-(\d+)(?:-.*)?$")]
    private static partial Regex ChapterDir();

    [GeneratedRegex(@"^scene-(\d+)\.md$")]
    private static partial Regex SceneFile();

    [GeneratedRegex(@"^scene-(\d+)-prose\.md$")]
    private static partial Regex ProseFile();

    [GeneratedRegex(@"^\s{0,3}(?:\d+[.)]|-)\s+(.*)$")]
    private static partial Regex BeatItem();

    [GeneratedRegex(@"^(?:act|chapter)-\d+-?")]
    private static partial Regex LayoutPrefix();
}
