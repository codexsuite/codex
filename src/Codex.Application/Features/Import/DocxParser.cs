using System.IO.Packaging;
using System.Text.RegularExpressions;
using System.Xml;
using Codex.Contracts.Errors;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Codex.Application.Features.Import;

// A Word document as one book in one act (Act 1):
//   Title / Subtitle style           -> the book's title and subtitle (else the file's title property, else its name)
//   Heading 1                        -> a chapter (Heading 2 or outline level 1 likewise names a scene)
//   Heading 2                        -> a scene, titled
//   a section break                  -> the next paragraph starts a new, untitled scene
//   other paragraphs                 -> the current scene's prose, italics and bold kept (ImportProse.ToStored)
//   text before the first Heading 1  -> a "Front matter" chapter, not numbered
// Images, comments, footnotes and endnotes are not imported and are listed as skipped. A document with no Heading 1 has
// no chapters to find and is refused. Registered in ImportServices.
public sealed partial class DocxParser : IImportParser
{
    private const string What = "Word document (.docx)";
    private const string ActTitle = "Act 1";
    private const string FrontMatterTitle = "Front matter";

    public string Source => "docx";

    public ImportPlan Parse(ImportUpload upload)
    {
        var file = ImportArchive.Single(upload, ".docx", What);
        CheckPackage(file.Content);
        try
        {
            var settings = new OpenSettings { MaxCharactersInPart = DocxPackageGuard.MaxMainPartChars };
            using var document = WordprocessingDocument.Open(new MemoryStream(file.Content), false, settings);
            return Build(file, upload.Omitted, document);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or FileFormatException or XmlException
                                      or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw ApiException.Validation("This is not a readable Word document: it could not be opened as .docx.");
        }
    }

    private static void CheckPackage(byte[] content)
    {
        using var archive = ImportArchive.Open(content, What);
        var entries = ImportArchive.Files(archive, What);
        ImportArchive.CheckExpansion(entries, What);
        DocxPackageGuard.Check(entries, What);
    }

    private static ImportPlan Build(ImportFile file, IReadOnlyList<ImportSkippedItem> omitted, WordprocessingDocument document)
    {
        var main = document.MainDocumentPart ?? throw ApiException.Validation("This Word document has no body.");
        var body = main.Document?.Body ?? throw ApiException.Validation("This Word document has no body.");
        var styles = StyleNames(main);

        var layout = new Layout();
        string? title = null;
        string? subtitle = null;
        var split = false;
        foreach (var paragraph in body.Descendants<Paragraph>().Where(p => !p.Ancestors<Run>().Any()))
        {
            var kind = KindOf(paragraph, styles);
            var text = kind == Kind.Body ? DocxText.Markdown(paragraph) : DocxText.Plain(paragraph);
            if (text.Length > 0)
            {
                switch (kind)
                {
                    case Kind.Title when title == null:
                        title = text;
                        break;
                    case Kind.Subtitle when subtitle == null:
                        subtitle = text;
                        break;
                    case Kind.Chapter:
                        layout.StartChapter(text);
                        split = false;
                        break;
                    case Kind.Scene:
                        layout.StartScene(text);
                        split = false;
                        break;
                    default:
                        layout.AddProse(kind == Kind.Body ? text : DocxText.Markdown(paragraph), split);
                        split = false;
                        break;
                }
            }
            split |= paragraph.ParagraphProperties?.SectionProperties != null && kind == Kind.Body;
        }
        if (layout.Chapters.All(c => !c.Numbered))
        {
            throw ApiException.Validation(
                "This document has no Heading 1 paragraphs, so there are no chapters to find. Style each chapter title as Heading 1 and try again.");
        }

        var skipped = omitted.ToList();
        AddNotImported(skipped, body, main);
        var bookTitle = title ?? document.PackageProperties.Title;
        if (string.IsNullOrWhiteSpace(bookTitle))
        {
            bookTitle = Path.GetFileNameWithoutExtension(file.Path).Replace('-', ' ').Replace('_', ' ').Trim();
        }
        var chapters = layout.Chapters.Select(c => new ImportChapterPlan(
            MarkdownFolderParser.Clip(c.Title), null, null, c.Numbered, c.Scenes.Select((s, n) => ScenePlan(c, s, n + 1, skipped)).ToList())).ToList();
        return new ImportPlan(
            file.Path,
            1 + omitted.Count,
            new ImportBookPlan(
                MarkdownFolderParser.Clip(bookTitle.Length > 0 ? bookTitle : "Imported book"),
                subtitle == null ? null : MarkdownFolderParser.Clip(subtitle),
                null,
                null,
                null),
            [new ImportActPlan(ActTitle, null, chapters)],
            [],
            skipped);
    }

    // A scene whose text is over the per-file prose limit comes across empty, listed as skipped, as an oversize Markdown file does.
    private static ImportScenePlan ScenePlan(ChapterBuffer chapter, SceneBuffer scene, int number, List<ImportSkippedItem> skipped)
    {
        if (scene.TooLarge)
        {
            skipped.Add(new ImportSkippedItem($"{chapter.Title}, scene {number}", ImportLimits.TooLarge));
        }
        var prose = scene.Paragraphs.Count == 0 ? "" : ImportProse.ToStored(string.Join("\n\n", scene.Paragraphs));
        return new ImportScenePlan(
            scene.Title is { } t ? MarkdownFolderParser.Clip(t) : null,
            null,
            null,
            prose.Length > 0 ? "draft" : "outline",
            null,
            false,
            [],
            prose.Length > 0 ? prose : null,
            []);
    }

    private static void AddNotImported(List<ImportSkippedItem> skipped, Body body, MainDocumentPart main)
    {
        var images = body.Descendants<Drawing>().Count() + body.Descendants<Picture>().Count();
        if (images > 0)
        {
            skipped.Add(new ImportSkippedItem("Images", $"{images} image{(images == 1 ? " is" : "s are")} not imported"));
        }
        var comments = main.WordprocessingCommentsPart?.Comments?.Elements<Comment>().Count() ?? 0;
        if (comments > 0)
        {
            skipped.Add(new ImportSkippedItem("Comments", $"{comments} comment{(comments == 1 ? " is" : "s are")} not imported"));
        }
        var notes = (main.FootnotesPart?.Footnotes?.Elements<Footnote>().Count(f => f.Type == null) ?? 0)
            + (main.EndnotesPart?.Endnotes?.Elements<Endnote>().Count(e => e.Type == null) ?? 0);
        if (notes > 0)
        {
            skipped.Add(new ImportSkippedItem("Footnotes and endnotes", $"{notes} note{(notes == 1 ? " is" : "s are")} not imported"));
        }
    }

    private static Dictionary<string, string> StyleNames(MainDocumentPart main) =>
        (main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
            .Where(s => s.StyleId?.Value != null && s.StyleName?.Val?.Value != null)
            .GroupBy(s => s.StyleId!.Value!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().StyleName!.Val!.Value!, StringComparer.Ordinal);

    private static Kind KindOf(Paragraph paragraph, Dictionary<string, string> styles)
    {
        var properties = paragraph.ParagraphProperties;
        var id = properties?.ParagraphStyleId?.Val?.Value;
        if (id != null)
        {
            var name = styles.GetValueOrDefault(id) ?? id;
            if (HeadingName().Match(name) is { Success: true } heading)
            {
                return heading.Groups[1].Value switch { "1" => Kind.Chapter, "2" => Kind.Scene, _ => Kind.Body };
            }
            if (string.Equals(name, "Title", StringComparison.OrdinalIgnoreCase))
            {
                return Kind.Title;
            }
            if (string.Equals(name, "Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                return Kind.Subtitle;
            }
        }
        return properties?.OutlineLevel?.Val?.Value switch { 0 => Kind.Chapter, 1 => Kind.Scene, _ => Kind.Body };
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingName();

    private enum Kind
    {
        Body,
        Title,
        Subtitle,
        Chapter,
        Scene,
    }

    private sealed class SceneBuffer(string? title)
    {
        private readonly List<string> _paragraphs = [];

        public string? Title { get; } = title;

        private long _length;

        public IReadOnlyList<string> Paragraphs => _paragraphs;

        // True once the scene's text passed ImportLimits.MaxFileBytes; its text is dropped and no more is kept.
        public bool TooLarge { get; private set; }

        public void Add(string markdown)
        {
            if (TooLarge)
            {
                return;
            }
            _length += markdown.Length;
            if (_length > ImportLimits.MaxFileBytes)
            {
                TooLarge = true;
                _paragraphs.Clear();
                return;
            }
            _paragraphs.Add(markdown);
        }
    }

    private sealed class ChapterBuffer(string title, bool numbered)
    {
        private readonly List<SceneBuffer> _scenes = [];

        public string Title { get; } = title;

        public bool Numbered { get; } = numbered;

        public IReadOnlyList<SceneBuffer> Scenes => _scenes;

        public SceneBuffer StartScene(string? sceneTitle)
        {
            var scene = new SceneBuffer(sceneTitle);
            _scenes.Add(scene);
            return scene;
        }
    }

    private sealed class Layout
    {
        private readonly List<ChapterBuffer> _chapters = [];

        public IReadOnlyList<ChapterBuffer> Chapters => _chapters;

        public void StartChapter(string title) => _chapters.Add(new ChapterBuffer(title, true));

        public void StartScene(string title) => Current().StartScene(title);

        public void AddProse(string markdown, bool newScene)
        {
            var chapter = Current();
            var scene = chapter.Scenes.Count == 0 || newScene ? chapter.StartScene(null) : chapter.Scenes[^1];
            scene.Add(markdown);
        }

        // Text before the first Heading 1 goes in a chapter of its own that the book does not number.
        private ChapterBuffer Current()
        {
            if (_chapters.Count == 0)
            {
                _chapters.Add(new ChapterBuffer(FrontMatterTitle, false));
            }
            return _chapters[^1];
        }
    }
}
