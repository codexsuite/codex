using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.Application.Features.Import;

// A pandoc book folder (the campaign folder of issue #13: Core/Players Guide/): chapter files of Markdown with raw
// LaTeX for the rpgtex dndbook class, and the images they place. docs/import.md has the whole mapping.
// - Order: a pandoc defaults .yaml with input-files names the chapter files; without one, top-level files named
//   "Chapter N.md" in number order. Everything else is listed as skipped.
// - Structure: each file is an act (its \part opener names it and gives its art; a later \part starts another act);
//   each "# 3.1 Fighter" heading is a chapter "Fighter" holding one scene with the text up to the next one. Text before
//   a file's first heading opens that file's first chapter (a file with no heading is one chapter named after its act).
// - PandocChapterReader maps the LaTeX; this class turns its blocks into stored prose, figures, openers and Spells
//   entries, and asks for the Fantasy RPG layout with drop caps when the source used \DndDropCapLine.
// Registered in ImportServices.
public sealed partial class PandocFolderParser : IImportParser
{
    public const string SpellsCategory = "Spells";

    public const string Template = "fantasy-rpg";

    public const string DropCapsOption = "drop-caps";

    public const string NotUsedImage = "An image the chapters do not use";

    public const string MissingImage = "Not in the folder, so it was left out";

    public static readonly IReadOnlyList<ImportFieldPlan> SpellFields =
    [
        new("level-school", "Level and school"),
        new("casting-time", "Casting time"),
        new("range", "Range"),
        new("components", "Components"),
        new("duration", "Duration"),
    ];

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public string Source => "pandoc";

    public static bool IsImage(string path) => ImageExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public ImportPlan Parse(ImportUpload upload)
    {
        var skipped = new List<ImportSkippedItem>();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in upload.Files)
        {
            if (!IsText(file.Path))
            {
                skipped.Add(new ImportSkippedItem(file.Path, IsImage(file.Path) ? NotUsedImage : ImportLimits.NotMarkdown));
            }
            else if (file.Content.Length > ImportLimits.MaxFileBytes)
            {
                skipped.Add(new ImportSkippedItem(file.Path, ImportLimits.TooLarge));
            }
            else if (Decode(file.Content) is { } text)
            {
                texts[file.Path] = text;
            }
            else
            {
                skipped.Add(new ImportSkippedItem(file.Path, "Not UTF-8 text"));
            }
        }

        var (root, order, defaultsPath, defaults) = FindBook(texts)
            ?? throw ApiException.Validation("This folder has no pandoc defaults file (a .yaml with input-files) and no \"Chapter N.md\" files, so there is nothing to import.");
        var chapters = new List<(string Path, string Text)>();
        foreach (var path in order)
        {
            if (texts.TryGetValue(path, out var text))
            {
                chapters.Add((path, text));
            }
            else
            {
                skipped.Add(new ImportSkippedItem(path, defaultsPath == null ? "Not readable" : $"Listed in {LastSegment(defaultsPath)} but not in the folder"));
            }
        }
        foreach (var path in texts.Keys.Where(p => p != defaultsPath && !order.Contains(p)).Order(StringComparer.Ordinal))
        {
            skipped.Add(new ImportSkippedItem(path, defaultsPath == null
                ? "Not a chapter file (Chapter N.md)"
                : $"Not one of the chapters {LastSegment(defaultsPath)} lists"));
        }

        var images = new Images(root, upload);
        var builder = new BookBuilder(images, skipped);
        foreach (var (path, text) in chapters)
        {
            builder.AddFile(path, text);
        }

        // Images sent or named that no chapter places, and the client's own list of what it left out.
        skipped.AddRange((upload.Images ?? []).Where(i => !builder.Places(i.Path)).Select(i => new ImportSkippedItem(i.Path, NotUsedImage)));
        foreach (var item in upload.Omitted)
        {
            if (!(IsImage(item.Path) && builder.Places(item.Path)))
            {
                skipped.Add(item.Reason == ImportLimits.NotMarkdown && IsImage(item.Path) ? item with { Reason = NotUsedImage } : item);
            }
        }
        if (builder.Entries.Dropped > 0)
        {
            skipped.Add(new ImportSkippedItem(FolderName(root), $"Only the first {ImportLimits.MaxEntries} knowledge base entries are imported; {builder.Entries.Dropped} more spells stay as LaTeX"));
        }

        var title = defaults?.Metadata.GetValueOrDefault("title") ?? FolderName(root);
        var book = new ImportBookPlan(
            MarkdownFolderParser.Clip(title), defaults?.Metadata.GetValueOrDefault("subtitle"), defaults?.Metadata.GetValueOrDefault("author"), null, null);
        LayoutSelectionRequest? layout = null;
        if (builder.UsesDndBook)
        {
            layout = new LayoutSelectionRequest(Template, null, builder.UsesDropCaps
                ? new Dictionary<string, JsonElement> { [DropCapsOption] = JsonSerializer.SerializeToElement(true) }
                : null);
        }
        var categories = builder.Entries.Items.Any(e => e.Category == SpellsCategory)
            ? new List<ImportCategoryPlan> { new(SpellsCategory, "spell", SpellFields) }
            : [];
        var fileCount = upload.Files.Count + upload.Omitted.Count + (upload.Images?.Count ?? 0);
        return new ImportPlan(
            FolderName(root), fileCount, book, builder.BuildActs(), builder.Entries.Items, skipped.OrderBy(s => s.Path, StringComparer.Ordinal).ToList(), categories, layout);
    }

    // The defaults file with input-files nearest the top (then by name), else the shallowest folder with Chapter N.md.
    private static (string Root, List<string> Order, string? DefaultsPath, PandocDefaults? Defaults)? FindBook(Dictionary<string, string> texts)
    {
        var defaults = texts
            .Where(t => t.Key.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || t.Key.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .Select(t => (Path: t.Key, Defaults: PandocDefaults.Parse(t.Value)))
            .Where(d => d.Defaults.InputFiles.Count > 0)
            .OrderBy(d => d.Path.Count(c => c == '/'))
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (defaults.Path != null)
        {
            var root = ParentOf(defaults.Path);
            var order = defaults.Defaults.InputFiles.Select(f => Join(root, f.TrimStart('.', '/'))).Distinct(StringComparer.Ordinal).ToList();
            return (root, order, defaults.Path, defaults.Defaults);
        }

        var chapters = texts.Keys
            .Select(p => (Path: p, Match: ChapterFile().Match(LastSegment(p))))
            .Where(c => c.Match.Success)
            .ToList();
        if (chapters.Count == 0)
        {
            return null;
        }
        var top = chapters.Min(c => c.Path.Count(ch => ch == '/'));
        var folder = chapters.Where(c => c.Path.Count(ch => ch == '/') == top).Select(c => ParentOf(c.Path)).Order(StringComparer.Ordinal).First();
        var ordered = chapters
            .Where(c => ParentOf(c.Path) == folder)
            .OrderBy(c => int.Parse(c.Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(c => c.Path, StringComparer.Ordinal)
            .Select(c => c.Path)
            .ToList();
        return (folder, ordered, null, null);
    }

    private static bool IsText(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    private static string? Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF').Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string FolderName(string root) => root.Length == 0 ? "Pandoc book" : LastSegment(root);

    private static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string ParentOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    private static string Join(string folder, string path) => folder.Length == 0 ? path : folder + "/" + path;

    [GeneratedRegex(@"^Chapter\s*0*(\d{1,6})\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterFile();

    // The images an import can place: sent with it (ImportUpload.Images) or, for a preview, named as left out.
    private sealed class Images
    {
        private readonly string _root;
        private readonly Dictionary<string, string> _present = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _byLowerCase = new(StringComparer.Ordinal);
        private readonly HashSet<string> _tooLarge = new(StringComparer.Ordinal);

        public Images(string root, ImportUpload upload)
        {
            _root = root;
            var named = (upload.Images ?? []).Select(i => i.Path)
                .Concat(upload.Omitted.Where(o => o.Reason == ImportLimits.NotMarkdown).Select(o => o.Path))
                .Where(IsImage);
            foreach (var path in named)
            {
                Add(path);
            }
            foreach (var path in upload.Omitted.Where(o => o.Reason == ImportLimits.TooLarge && IsImage(o.Path)).Select(o => o.Path))
            {
                _tooLarge.Add(path);
                Add(path);
            }
        }

        // The upload path of a reference ("images/x%20y.png" already decoded), matched exactly or else ignoring case
        // (the folder comes from Windows); a reference that climbs out of the folder is never found.
        public (string? Path, string? Problem, string Display) Resolve(string reference)
        {
            var clean = reference.Replace('\\', '/').Trim();
            while (clean.StartsWith("./", StringComparison.Ordinal))
            {
                clean = clean[2..];
            }
            var display = Join(_root, clean);
            if (clean.Length == 0 || clean.StartsWith('/') || clean.Split('/').Any(s => s is ".." or "."))
            {
                return (null, MissingImage, display);
            }
            if (!IsImage(clean))
            {
                return (null, "Not a PNG, JPEG or WebP image, so it was left out", display);
            }
            var path = _present.GetValueOrDefault(display)
                       ?? (_byLowerCase.TryGetValue(display.ToLowerInvariant(), out var matches) && matches.Count == 1 ? matches[0] : null);
            if (path == null)
            {
                return (null, MissingImage, display);
            }
            return _tooLarge.Contains(path) ? (null, $"Too large (over {ImportLimits.MaxImageBytes / (1024 * 1024)} MB), so it was left out", path) : (path, null, path);
        }

        private void Add(string path)
        {
            _present[path] = path;
            var key = path.ToLowerInvariant();
            if (!_byLowerCase.TryGetValue(key, out var list))
            {
                _byLowerCase[key] = list = [];
            }
            if (!list.Contains(path))
            {
                list.Add(path);
            }
        }
    }

    // Acts, chapters and scenes as the chapter files are read, in order.
    private sealed partial class BookBuilder(Images images, List<ImportSkippedItem> skipped)
    {
        private readonly List<ActBuilder> _acts = [];
        private readonly HashSet<string> _imagePaths = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

        public ImportEntryList Entries { get; } = new();

        public bool UsesDndBook { get; private set; }

        public bool UsesDropCaps { get; private set; }

        public bool Places(string path) => _imagePaths.Contains(path);

        public IReadOnlyList<ImportActPlan> BuildActs() => _acts.Select(a => a.Build()).ToList();

        public void AddFile(string path, string text)
        {
            UsesDropCaps |= text.Contains("\\DndDropCapLine", StringComparison.Ordinal);
            UsesDndBook |= UsesDropCaps || DndCommand().IsMatch(text);
            var fileName = LastSegment(path);
            var act = new ActBuilder(MarkdownFolderParser.Clip(fileName[..^Path.GetExtension(fileName).Length]));
            _acts.Add(act);
            var lead = new List<PandocBlock>();
            ChapterBuilder? chapter = null;
            foreach (var block in PandocChapterReader.Read(text))
            {
                switch (block.Kind)
                {
                    case PandocBlockKind.Part when act.Chapters.Count == 0 && !act.Named:
                        Name(act, block);
                        break;
                    case PandocBlockKind.Part:
                        act = new ActBuilder(MarkdownFolderParser.Clip(block.Title ?? act.Title));
                        Name(act, block);
                        _acts.Add(act);
                        chapter = null;
                        break;
                    case PandocBlockKind.Chapter:
                        chapter = new ChapterBuilder(MarkdownFolderParser.Clip(block.Title!));
                        act.Chapters.Add(chapter);
                        chapter.Blocks.AddRange(lead);
                        lead.Clear();
                        break;
                    default:
                        (chapter?.Blocks ?? lead).Add(block);
                        break;
                }
            }
            if (lead.Count > 0)
            {
                var only = new ChapterBuilder(act.Title);
                only.Blocks.AddRange(lead);
                act.Chapters.Add(only);
            }
            foreach (var built in _acts.SelectMany(a => a.Chapters).Where(c => c.Scene == null))
            {
                built.Scene = Scene(built.Blocks);
            }
        }

        private void Name(ActBuilder act, PandocBlock part)
        {
            act.Title = MarkdownFolderParser.Clip(part.Title is { Length: > 0 } title ? title : act.Title);
            act.Named = true;
            if (part.Image != null && Image(part.Image, "the act has no opener art") is { } image)
            {
                act.Opener = new ImportOpenerPlan(image, part.Caption);
            }
        }

        private ImportScenePlan Scene(List<PandocBlock> blocks)
        {
            var figures = new List<ImportFigurePlan>();
            var entries = new List<string>();
            var body = Store(blocks, figures, entries);
            var prose = body.Count == 0 ? null : string.Join("\n\n", body) + "\n";
            return new ImportScenePlan(null, null, null, prose == null ? "outline" : "draft", null, false, [], prose, entries, figures);
        }

        // Stored blocks for the scene (or, without figures, for an entry's body), in order.
        private List<string> Store(IEnumerable<PandocBlock> blocks, List<ImportFigurePlan>? figures, List<string> entries)
        {
            var output = new List<string>();
            foreach (var block in blocks)
            {
                switch (block.Kind)
                {
                    case PandocBlockKind.Text:
                        Add(output, ImportProse.ToStored(block.Text!).TrimEnd('\n'));
                        break;
                    case PandocBlockKind.Latex:
                        if (block.Text!.Trim().Length > 0)
                        {
                            output.Add(ImportProse.Fence("{=latex}", block.Text));
                        }
                        break;
                    case PandocBlockKind.Table:
                        output.Add(ImportProse.Table(block.Text!.Split('\n')));
                        break;
                    case PandocBlockKind.Comment:
                        output.Add(ImportProse.Opaque("comment", block.Text!));
                        break;
                    case PandocBlockKind.Break:
                        output.Add("::pagebreak");
                        break;
                    case PandocBlockKind.Figure when figures != null:
                        if (Image(block.Image!, "the figure was left out") is { } image)
                        {
                            var figure = new ImportFigurePlan(Guid.CreateVersion7(), image, block.Placement ?? "column", block.Caption);
                            figures.Add(figure);
                            output.Add($"::figure{{id=\"{figure.Id}\"}}");
                        }
                        break;
                    case PandocBlockKind.Spell:
                        output.Add(Spell(block, entries));
                        break;
                    case PandocBlockKind.Container:
                        var inner = Store(block.Children ?? [], figures, entries);
                        var header = block.Container == "sidebar" ? $":::sidebar{{title=\"{SidebarTitle(block.Title)}\"}}" : ":::" + block.Container;
                        output.Add(inner.Count == 0 ? header + "\n:::" : header + "\n" + string.Join("\n\n", inner) + "\n:::");
                        break;
                    default:
                        // A figure in a spell's own text, or a part or chapter nothing placed: kept as LaTeX-free text.
                        Add(output, ImportProse.ToStored(block.Title ?? block.Caption ?? "").TrimEnd('\n'));
                        break;
                }
            }
            return output;
        }

        private string Spell(PandocBlock block, List<string> entries)
        {
            var key = "spell:" + block.Title!.ToLowerInvariant();
            var existing = Entries.Items.FirstOrDefault(e => e.Key == key);
            var id = existing?.Id ?? Guid.CreateVersion7();
            if (existing == null)
            {
                var body = Store(block.Children ?? [], null, []);
                var plan = new ImportEntryPlan(key, SpellsCategory, MarkdownFolderParser.Clip(block.Title), null, null,
                    body.Count == 0 ? null : string.Join("\n\n", body) + "\n", null, id, block.Fields);
                if (!Entries.Add(plan))
                {
                    return ImportProse.Fence("{=latex}", block.Text!);
                }
            }
            if (!entries.Contains(key))
            {
                entries.Add(key);
            }
            return $"::spell{{kb=\"{id}\"}}";
        }

        // The upload path of a referenced image, or null (listed as skipped once) when it cannot be placed.
        private string? Image(string reference, string consequence)
        {
            var (path, problem, display) = images.Resolve(reference);
            if (path != null)
            {
                _imagePaths.Add(path);
                return path;
            }
            if (_reported.Add(display))
            {
                skipped.Add(new ImportSkippedItem(display, $"{problem}; {consequence}"));
            }
            return null;
        }

        private static void Add(List<string> output, string block)
        {
            if (block.Length > 0)
            {
                output.Add(block);
            }
        }

        // A title the reader rules accept: no double quote (attribute values are not escaped).
        private static string SidebarTitle(string? title) => (title ?? "").Replace('"', '\'').Trim();

        [GeneratedRegex(@"\\(?:Dnd[A-Za-z]+|begin\{Dnd)")]
        private static partial Regex DndCommand();
    }

    private sealed class ActBuilder(string title)
    {
        public string Title { get; set; } = title;

        public bool Named { get; set; }

        public ImportOpenerPlan? Opener { get; set; }

        internal List<ChapterBuilder> Chapters { get; } = [];

        public ImportActPlan Build() => new(Title, null, Chapters.Select(c => c.Build()).ToList(), Opener);
    }

    private sealed class ChapterBuilder(string title)
    {
        internal List<PandocBlock> Blocks { get; } = [];

        public ImportScenePlan? Scene { get; set; }

        public ImportChapterPlan Build() => new(title, null, null, true, Scene == null ? [] : [Scene]);
    }
}
