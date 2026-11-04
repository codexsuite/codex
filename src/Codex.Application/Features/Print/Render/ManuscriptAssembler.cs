using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Codex.Application.Features.Print.Templates;
using Codex.Application.Prose;

namespace Codex.Application.Features.Print.Render;

// Scene / chapter / book -> pandoc AST (issue #13 part 2). One source for every output: the template's Lua filters
// and wrapper.tex decide how it looks. Template-neutral output:
//   act            Header 1 (#act-<id>, a \part), left out when the act has no title or the scope is not the book
//   chapter        Header 2 (#ch-<id>; class unnumbered for front matter); opener art as attributes opener and
//                  opener-caption on either header
//   scene          its blocks; Div.scene-break between two scenes of a chapter; prose headings move down two levels
//   ::figure       Figure (class = placement) of the rendition, or Div.missing-figure with the caption when the asset
//                  or its rendition is missing (flagged)
//   ::spell, ::statblock   Div.spell / Div.statblock with attributes title and data-field-<key> (only keys of
//                  lower-case letters, digits and hyphens, after _ -> -, so nothing becomes a live HTML attribute), holding
//                  Div.entry-heading, Div.entry-fields (a definition list: the fallback look) and the entry's body
//   ::pagebreak    Div.pagebreak          :::readaloud / :::sidebar / :::aside   Div.<name> (title attribute)
//   ::beat, ::kb-addition   nothing (planning objects)
//   pipe table     Table (alignment from the delimiter row, cells Plain with full inline content)   ---   HorizontalRule
//   <!-- … -->     dropped, not flagged unless it hides text: after its `-->` on the line, or an unclosed one (hidden_by_comment)   ~~x~~  Strikeout   [x](https://…)  Link   `x`{=latex}  RawInline latex, as the block
//   ```{=latex}    RawBlock latex when the template allows raw LaTeX, else dropped; always flagged;
//                  Quick preview (Html) draws a Div.raw-latex placeholder instead (Span.raw-latex for an inline span)
//   other fences   CodeBlock          anything opaque (unknown directives)   dropped, flagged
//   [x](kb:id)     its text           [x](chapter:id)   a link to #ch-<id> when that chapter is rendered, else text;
//                  text that is the chapter's title becomes "Chapter N: Title" (numbered) or the current title
//   [x](ref:id)    the passage, then a footnote (template citations "footnote") or "(Author Year, locator)"
//   [^](ref:id)    the footnote or the parenthetical alone
// Pure.
public static partial class ManuscriptAssembler
{
    private const int HeadingShift = 2;

    [GeneratedRegex("^:::(?<name>[a-z][a-z0-9-]*)(?:\\{title=\"(?<title>[^\"]*)\"\\})?$")]
    private static partial Regex ContainerLine();

    [GeneratedRegex("^(?<marker>-|\\d+\\.)(?: |$)")]
    private static partial Regex ListItem();

    [GeneratedRegex("^(#{1,6}) (.*)$")]
    private static partial Regex HeadingLine();

    // Names that may become an attribute (data-field-<key>) or a class (a fence's language).
    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex SafeName();

    // Quick preview draws raw LaTeX as this label; only a Proof renders it.
    private const string RawLatexPlaceholder = "Raw LaTeX, shown in Proof";

    public const string FieldAttributePrefix = "data-field-";

    // Metadata the assembler sets; a template option may not use these names (toc is an option on purpose).
    public static readonly IReadOnlySet<string> ReservedMeta = new HashSet<string>(StringComparer.Ordinal)
    {
        "title", "subtitle", "author", "codex-scope", "codex-trim", "title-page", "papersize", "page-width", "page-height",
        "margin-top", "margin-bottom", "margin-inner", "margin-outer", "column-gap", "columns", "onecolumn",
    };

    public static AssembledDocument Assemble(
        ManuscriptSnapshot snapshot,
        TemplatePackage template,
        string trim,
        IReadOnlyDictionary<string, JsonElement> options,
        RenderTarget target,
        string toolchain = "",
        bool sceneMarkers = false)
    {
        var context = new Context(snapshot, template, target);
        var blocks = new JsonArray();
        var chapters = snapshot.Chapters.Where(c => c.InScope).ToList();
        Guid? act = null;
        foreach (var chapter in chapters)
        {
            var scenes = snapshot.Scenes.Where(s => s.ChapterId == chapter.Id).ToList();
            if (snapshot.Scope != RenderScope.Scene)
            {
                if (snapshot.Scope == RenderScope.Book && chapter.ActId != act
                    && snapshot.Acts.FirstOrDefault(a => a.Id == chapter.ActId) is { Title.Length: > 0 } actRow)
                {
                    blocks.Add(Heading(context, 1, "act-" + actRow.Id, actRow.Title, [], actRow.OpenerAssetId, actRow.OpenerCaption));
                }

                blocks.Add(Heading(context, 2, ChapterAnchor(chapter.Id), chapter.Title,
                    chapter.IsNumbered ? [] : ["unnumbered"], chapter.OpenerAssetId, chapter.OpenerCaption));
            }

            act = chapter.ActId;
            for (var i = 0; i < scenes.Count; i++)
            {
                if (i > 0)
                {
                    blocks.Add(PandocAst.Div(PandocAst.Attr(classes: ["scene-break"]), []));
                }

                context.Scene = scenes[i].Id;
                if (sceneMarkers)
                {
                    blocks.Add(SceneMarker(chapter, scenes[i].Id, i + 1));
                }

                foreach (var block in Blocks(context, scenes[i].Body))
                {
                    blocks.Add(block);
                }
            }
        }

        var meta = Meta(snapshot, template, trim, options);
        var ast = PandocAst.Document(meta, blocks);
        return new AssembledDocument(
            ast, context.Images.Values.ToList(), context.Flags, template.Slug, template.Version, trim, options,
            Hash(ast, template, trim, options, toolchain, context.Images.Values));
    }

    // Quick preview only (sceneMarkers): an empty Div that names the scene the blocks after it belong to, which the Layout view turns
    // into data-scene on each block (hover label, Open in Write) and removes. Never in a PDF, so proofs keep their hash.
    private static JsonNode SceneMarker(RenderChapter chapter, Guid sceneId, int sceneNumber) =>
        PandocAst.Div(
            PandocAst.Attr(classes: ["codex-scene"], attributes:
            [
                ("data-scene", sceneId.ToString()),
                ("data-chapter", chapter.Id.ToString()),
                ("data-label", (chapter.Number is { } number ? $"Ch {number}" : "Front") + $" · Sc {sceneNumber}"),
            ]),
            []);

    public static string ChapterAnchor(Guid chapterId) => "ch-" + chapterId;

    private sealed class Context(ManuscriptSnapshot snapshot, TemplatePackage template, RenderTarget target)
    {
        public ManuscriptSnapshot Snapshot { get; } = snapshot;

        public TemplatePackage Template { get; } = template;

        public RenderTarget Target { get; } = target;

        public Guid? Scene { get; set; }

        private readonly List<RenderFlag> _flags = [];
        private readonly Dictionary<Guid, PlacedImage> _images = [];

        public IReadOnlyList<RenderFlag> Flags => _flags;

        public IReadOnlyDictionary<Guid, PlacedImage> Images => _images;

        public void Flag(string kind, string detail, bool kept = false) => _flags.Add(new RenderFlag(kind, Scene, detail, kept));

        // The image source for an asset's rendition, or null (flagged) when there is none to place.
        public string? Place(Guid assetId, string what)
        {
            if (_images.TryGetValue(assetId, out var placed))
            {
                return placed.Source;
            }

            if (!Snapshot.Images.TryGetValue(assetId, out var rendition))
            {
                Flag(RenderFlag.MissingAsset, $"{what}: asset {assetId} has no image to place");
                return null;
            }

            var source = Target == RenderTarget.Pdf
                ? "images/" + assetId.ToString("N") + rendition.Extension
                : $"/api/assets/{assetId}/renditions/{rendition.Purpose}";
            _images[assetId] = new PlacedImage(assetId, source, rendition);
            return source;
        }
    }

    private static JsonObject Heading(Context context, int level, string id, string title, string[] classes, Guid? opener, string? caption)
    {
        var attributes = new List<(string, string)>();
        if (opener is { } asset && context.Place(asset, "Opener art for " + title) is { } source)
        {
            attributes.Add(("opener", source));
            if (!string.IsNullOrWhiteSpace(caption))
            {
                attributes.Add(("opener-caption", caption));
            }
        }

        return PandocAst.Header(level, PandocAst.Attr(id, classes, attributes), PandocAst.Text(title));
    }

    // One Markdown body (a scene, a quote's or list item's inside, an entry's body) as pandoc blocks.
    private static List<JsonNode> Blocks(Context context, string text)
    {
        var document = ProseDocument.Parse(text);
        var output = new List<JsonNode>();
        var blocks = document.Blocks;
        var i = 0;
        while (i < blocks.Count)
        {
            var block = blocks[i];
            var source = text[block.Start..block.End];
            if (block.Kind == ProseBlockKind.ContainerMarker)
            {
                // Containers are not nested, so the next marker closes this one.
                var close = i + 1;
                while (close < blocks.Count && blocks[close].Kind != ProseBlockKind.ContainerMarker)
                {
                    close++;
                }

                var header = ContainerLine().Match(source);
                var inner = close < blocks.Count ? text[block.End..blocks[close].Start] : string.Empty;
                var attributes = header.Groups["title"].Success ? new[] { ("title", header.Groups["title"].Value) } : [];
                output.Add(PandocAst.Div(PandocAst.Attr(classes: [header.Groups["name"].Value], attributes: attributes), Blocks(context, inner)));
                i = close + 1;
                continue;
            }

            output.AddRange(Block(context, block, source));
            i++;
        }

        return output;
    }

    private static IEnumerable<JsonNode> Block(Context context, ProseBlock block, string source)
    {
        switch (block.Kind)
        {
            case ProseBlockKind.Paragraph:
                return [PandocAst.Para(Inlines(context, source))];
            case ProseBlockKind.Heading:
                var heading = HeadingLine().Match(source);
                var level = Math.Min(6, heading.Groups[1].Length + HeadingShift);
                return [PandocAst.Header(level, PandocAst.Attr(), Inlines(context, heading.Groups[2].Value))];
            case ProseBlockKind.Quote:
                var quoted = string.Join('\n', source.Split('\n').Select(l => l.StartsWith("> ", StringComparison.Ordinal) ? l[2..] : l[1..]));
                return [PandocAst.BlockQuote(Blocks(context, quoted))];
            case ProseBlockKind.List:
                return [List(context, source)];
            case ProseBlockKind.Table:
                return [Table(context, source)];
            case ProseBlockKind.Rule:
                return [PandocAst.HorizontalRule()];
            case ProseBlockKind.Opaque when source.StartsWith("<!--", StringComparison.Ordinal):
                FlagHiddenText(context, source);
                return [];
            case ProseBlockKind.Directive:
                return Directive(context, block.Directive!, block.Id);
            case ProseBlockKind.Fence:
                return Fence(context, source);
            default:
                context.Flag(RenderFlag.UnknownDirective, source.Split('\n')[0]);
                return [];
        }
    }

    // The comment itself is dropped without a flag, but text it hides is the author's: whatever follows `-->` on its
    // line (the reader keeps that line in the comment block), or everything after a `<!--` that never closes.
    private static void FlagHiddenText(Context context, string comment)
    {
        var close = comment.IndexOf("-->", 4, StringComparison.Ordinal);
        if (close < 0)
        {
            context.Flag(RenderFlag.HiddenByComment, "An unclosed HTML comment hides the rest of the scene");
        }
        else if (comment[(close + 3)..].Trim().Length > 0)
        {
            context.Flag(RenderFlag.HiddenByComment, "Text after an HTML comment is not printed");
        }
    }

    // A pipe table: cells are Plain blocks of full inline content; every row has as many cells as the widest row.
    private static JsonObject Table(Context context, string source)
    {
        var lines = source.Split('\n');
        var alignments = ProseTable.Alignments(lines[1]).ToList();
        var rows = lines.Where((_, index) => index != 1).Select(ProseTable.SplitRow).ToList();
        var columns = Math.Max(alignments.Count, rows.Max(r => r.Count));
        while (alignments.Count < columns)
        {
            alignments.Add(null);
        }

        List<JsonNode>[] Cells(IReadOnlyList<string> row) => Enumerable.Range(0, columns)
            .Select(c => c < row.Count ? new List<JsonNode> { PandocAst.Plain(Inlines(context, ProseTable.CellText(row[c]))) } : [])
            .ToArray();

        return PandocAst.Table(alignments, Cells(rows[0]), rows.Skip(1).Select(Cells));
    }

    private static JsonObject List(Context context, string source)
    {
        var items = new List<StringBuilder>();
        var width = 0;
        int? start = null;
        foreach (var line in source.Split('\n'))
        {
            var item = ListItem().Match(line);
            if (item.Success)
            {
                var marker = item.Groups["marker"].Value;
                start ??= marker == "-" ? null : int.Parse(marker.TrimEnd('.'), CultureInfo.InvariantCulture);
                width = marker.Length + 1;
                items.Add(new StringBuilder(line.Length > width ? line[width..] : string.Empty));
            }
            else
            {
                items[^1].Append('\n').Append(line.Length >= width && line[..width].Trim().Length == 0 ? line[width..] : line.TrimStart());
            }
        }

        // A tight item (one paragraph, or a paragraph then its nested list) starts with Plain, as pandoc reads one.
        var bodies = items.Select(i => ItemBlocks(context, i.ToString())).Select(blocks => blocks
            .Select((b, index) => index == 0 && b["t"]!.GetValue<string>() == "Para" && (blocks.Count == 1 || blocks[1]["t"]!.GetValue<string>() is "BulletList" or "OrderedList")
                ? PandocAst.Node("Plain", b["c"]!.DeepClone())
                : b)
            .ToList()).ToList();
        return start is { } first ? PandocAst.OrderedList(first, bodies) : PandocAst.BulletList(bodies);
    }

    // An item's lines (the marker and indent already cut): a list starting on a later line is nested in the item, which
    // the paragraph rules alone would swallow as text.
    private static List<JsonNode> ItemBlocks(Context context, string item)
    {
        var lines = item.Split('\n');
        var nested = Array.FindIndex(lines, 1, l => ListItem().IsMatch(l));
        if (nested < 0)
        {
            return Blocks(context, item);
        }

        return [.. Blocks(context, string.Join('\n', lines[..nested])), .. Blocks(context, string.Join('\n', lines[nested..]))];
    }

    private static IEnumerable<JsonNode> Directive(Context context, string name, Guid? id)
    {
        switch (name)
        {
            case "figure":
                return [Figure(context, id!.Value)];
            case "spell" or "statblock":
                return Entry(context, name, id!.Value);
            case "pagebreak":
                return [PandocAst.Div(PandocAst.Attr(classes: ["pagebreak"]), [])];
            default:
                return [];
        }
    }

    private static JsonNode Figure(Context context, Guid figureId)
    {
        if (!context.Snapshot.Figures.TryGetValue(figureId, out var figure))
        {
            context.Flag(RenderFlag.MissingAsset, $"Figure {figureId} is not in this book");
            return PandocAst.Div(PandocAst.Attr(classes: ["missing-figure"]), [PandocAst.Para(PandocAst.Text("Figure"))]);
        }

        var caption = figure.Caption ?? string.Empty;
        var source = context.Place(figure.AssetId, "Figure " + (caption.Length > 0 ? caption : figureId.ToString()));
        if (source is null)
        {
            return PandocAst.Div(
                PandocAst.Attr("fig-" + figureId, ["missing-figure", figure.Placement]),
                [PandocAst.Para(PandocAst.Text(caption.Length > 0 ? caption : figure.AltText ?? "Figure"))]);
        }

        var image = PandocAst.Image(PandocAst.Attr(attributes: [("width", "100%")]), PandocAst.Text(figure.AltText ?? caption), source);
        return PandocAst.Figure(PandocAst.Attr("fig-" + figureId, [figure.Placement]), Inlines(context, caption), [PandocAst.Plain([image])]);
    }

    private static IEnumerable<JsonNode> Entry(Context context, string kind, Guid entryId)
    {
        if (!context.Snapshot.Entries.TryGetValue(entryId, out var entry))
        {
            context.Flag(RenderFlag.MissingEntry, $"{kind} {entryId}");
            return [];
        }

        var fields = new List<(string Key, string Value)>();
        using (var json = JsonDocument.Parse(entry.Fields))
        {
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                fields.AddRange(json.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .Select(p => (p.Name.Replace('_', '-').ToLowerInvariant(), p.Value.ToString()))
                    .Where(f => SafeName().IsMatch(f.Item1)));
            }
        }

        var content = new List<JsonNode>
        {
            PandocAst.Div(PandocAst.Attr(classes: ["entry-heading"]), [PandocAst.Para([PandocAst.Strong(PandocAst.Text(entry.Name))])]),
        };
        if (fields.Count > 0)
        {
            content.Add(PandocAst.Div(PandocAst.Attr(classes: ["entry-fields"]), [PandocAst.DefinitionList(
                fields.Select(f => (PandocAst.Text(Label(f.Key)), (IEnumerable<JsonNode>)[PandocAst.Plain(PandocAst.Text(f.Value))])))]));
        }
        content.AddRange(Blocks(context, entry.Body));
        var attributes = fields.Select(f => (FieldAttributePrefix + f.Key, f.Value)).Prepend(("title", entry.Name));
        return [PandocAst.Div(PandocAst.Attr("entry-" + entryId, [kind], attributes), content)];
    }

    private static string Label(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..].Replace('-', ' ');

    private static IEnumerable<JsonNode> Fence(Context context, string source)
    {
        var lines = source.Split('\n');
        var info = lines[0][3..].Trim();
        var body = string.Join('\n', lines.Skip(1).Take(lines[^1] == "```" && lines.Length > 1 ? lines.Length - 2 : lines.Length - 1));
        if (info == "{=latex}")
        {
            var kept = context.Template.AllowsRawLatex;
            context.Flag(RenderFlag.RawLatex, body.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty, kept);
            if (kept && context.Target == RenderTarget.Html)
            {
                return [PandocAst.Div(PandocAst.Attr(classes: ["raw-latex"]), [PandocAst.Plain(PandocAst.Text(RawLatexPlaceholder))])];
            }

            return kept ? [PandocAst.RawBlock("latex", body)] : [];
        }

        return [PandocAst.CodeBlock(PandocAst.Attr(classes: SafeName().IsMatch(info) ? [info] : []), body)];
    }

    // Inline Markdown (ProseInline) as pandoc inlines: runs that share their outermost mark are wrapped together.
    private static List<JsonNode> Inlines(Context context, string text) => Wrap(context, ProseInline.Parse(text));

    private static List<JsonNode> Wrap(Context context, IReadOnlyList<InlineLeaf> leaves)
    {
        var output = new List<JsonNode>();
        var i = 0;
        while (i < leaves.Count)
        {
            var leaf = leaves[i];
            var outer = leaf.Marks.FirstOrDefault(m => m.Kind != InlineMarkKind.Code);
            if (outer is null)
            {
                output.AddRange(Leaf(context, leaf));
                i++;
                continue;
            }

            var run = new List<InlineLeaf>();
            while (i < leaves.Count && leaves[i].Marks.FirstOrDefault(m => m.Kind != InlineMarkKind.Code) == outer)
            {
                run.Add(leaves[i] with { Marks = leaves[i].Marks.Where(m => m != outer).ToList() });
                i++;
            }

            output.AddRange(Mark(context, outer, run));
        }

        return output;
    }

    private static IEnumerable<JsonNode> Leaf(Context context, InlineLeaf leaf) => leaf.Kind switch
    {
        InlineLeafKind.Break => [PandocAst.LineBreak()],
        InlineLeafKind.RefMarker => Citation(context, leaf.Id!.Value),
        InlineLeafKind.RawLatex => RawLatex(context, leaf.Text),
        _ when leaf.Marks.Any(m => m.Kind == InlineMarkKind.Code) => [PandocAst.Code(leaf.Text)],
        _ => PandocAst.Text(leaf.Text),
    };

    private static IEnumerable<JsonNode> RawLatex(Context context, string source)
    {
        var kept = context.Template.AllowsRawLatex;
        context.Flag(RenderFlag.RawLatex, source, kept);
        if (kept && context.Target == RenderTarget.Html)
        {
            return [PandocAst.Span(PandocAst.Attr(classes: ["raw-latex"]), PandocAst.Text(RawLatexPlaceholder))];
        }

        return kept ? [PandocAst.RawInline("latex", source)] : [];
    }

    private static IEnumerable<JsonNode> Mark(Context context, InlineMark mark, List<InlineLeaf> run)
    {
        var inner = Wrap(context, run);
        switch (mark.Kind)
        {
            case InlineMarkKind.Bold:
                return [PandocAst.Strong(inner)];
            case InlineMarkKind.Italic:
                return [PandocAst.Emph(inner)];
            case InlineMarkKind.Strike:
                return [PandocAst.Strikeout(inner)];
            case InlineMarkKind.Link:
                return [PandocAst.Link(PandocAst.Attr(), inner, mark.Href!)];
            case InlineMarkKind.Ref:
                return inner.Concat(Citation(context, mark.Id!.Value));
            case InlineMarkKind.ChapterLink:
                return ChapterLink(context, mark.Id!.Value, string.Concat(run.Select(l => l.Text)), inner);
            default:
                return inner;
        }
    }

    private static IEnumerable<JsonNode> ChapterLink(Context context, Guid chapterId, string text, List<JsonNode> inner)
    {
        var chapter = context.Snapshot.Chapters.FirstOrDefault(c => c.Id == chapterId);
        if (chapter is null)
        {
            return inner;
        }

        if (string.Equals(text.Trim(), chapter.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            inner = [.. PandocAst.Text(chapter.Number is { } n ? $"Chapter {n.ToString(CultureInfo.InvariantCulture)}: {chapter.Title}" : chapter.Title)];
        }

        return chapter.InScope ? [PandocAst.Link(PandocAst.Attr(), inner, "#" + ChapterAnchor(chapterId))] : inner;
    }

    private static IEnumerable<JsonNode> Citation(Context context, Guid useId)
    {
        if (!context.Snapshot.Citations.TryGetValue(useId, out var cite))
        {
            context.Flag(RenderFlag.MissingReference, $"Source use {useId}");
            return [];
        }

        var authors = AuthorLabel(cite.Authors);
        var year = cite.Year?.ToString(CultureInfo.InvariantCulture);
        if (context.Template.Citations == CitationForm.AuthorDate)
        {
            var parts = string.Join(", ", new[] { string.Join(' ', new[] { authors, year }.Where(p => !string.IsNullOrEmpty(p))), cite.Locator }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            return parts.Length == 0 ? [] : [PandocAst.Node("Space"), .. PandocAst.Text($"({parts})")];
        }

        var note = new List<JsonNode>();
        if (authors.Length > 0)
        {
            note.AddRange(PandocAst.Text(authors + ", "));
        }
        note.Add(PandocAst.Emph(PandocAst.Text(cite.Title.Length > 0 ? cite.Title : "Untitled source")));
        var published = string.Join(", ", new[] { cite.Publisher, year }.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (published.Length > 0)
        {
            note.AddRange(PandocAst.Text($" ({published})"));
        }
        if (!string.IsNullOrWhiteSpace(cite.Locator))
        {
            note.AddRange(PandocAst.Text(", " + cite.Locator));
        }
        note.Add(PandocAst.Node("Str", "."));
        return [PandocAst.Note([PandocAst.Para(note)])];
    }

    // "Beck", "Cagan and Jones", "Forsgren et al." from CSL-JSON names.
    private static string AuthorLabel(string authorsJson)
    {
        using var json = JsonDocument.Parse(authorsJson);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = json.RootElement.EnumerateArray()
            .Select(a => new[] { "literal", "family", "given" }
                .Select(k => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)))
            .OfType<string>()
            .ToList();
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} et al.",
        };
    }

    private static JsonObject Meta(ManuscriptSnapshot snapshot, TemplatePackage template, string trim, IReadOnlyDictionary<string, JsonElement> options)
    {
        var (width, height) = TrimSizes.Page(trim);
        var page = template.Page;
        string Inches(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var book = snapshot.Scope == RenderScope.Book;
        var chapter = snapshot.Chapters.FirstOrDefault(c => c.InScope);
        var meta = new JsonObject
        {
            ["title"] = PandocAst.MetaInlines(book || chapter is null ? snapshot.Book.Title : chapter.Title),
            ["codex-scope"] = PandocAst.MetaString(snapshot.Scope.ToString().ToLowerInvariant()),
            ["codex-trim"] = PandocAst.MetaString(trim),
            ["title-page"] = PandocAst.MetaBool(book),
            ["papersize"] = PandocAst.MetaString(trim.ToLowerInvariant() switch { "us-letter" or "8.5x11" => "letterpaper", "a4" => "a4paper", _ => string.Empty }),
            ["page-width"] = PandocAst.MetaString(Inches(width)),
            ["page-height"] = PandocAst.MetaString(Inches(height)),
            ["margin-top"] = PandocAst.MetaString(Inches(page.Top)),
            ["margin-bottom"] = PandocAst.MetaString(Inches(page.Bottom)),
            ["margin-inner"] = PandocAst.MetaString(Inches(page.Inner)),
            ["margin-outer"] = PandocAst.MetaString(Inches(page.Outer)),
            ["column-gap"] = PandocAst.MetaString(Inches(page.ColumnGap)),
            ["columns"] = PandocAst.MetaString(page.Columns.ToString(CultureInfo.InvariantCulture)),
            ["onecolumn"] = PandocAst.MetaBool(page.Columns == 1),
        };
        if (book && !string.IsNullOrWhiteSpace(snapshot.Book.Subtitle))
        {
            meta["subtitle"] = PandocAst.MetaInlines(snapshot.Book.Subtitle);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Book.AuthorName))
        {
            meta["author"] = PandocAst.Node("MetaList", new JsonArray(PandocAst.MetaInlines(snapshot.Book.AuthorName)));
        }

        foreach (var (name, value) in options)
        {
            meta[name] = value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? PandocAst.MetaBool(value.GetBoolean())
                : PandocAst.MetaString(value.ToString());
        }

        // The table of contents is a book-scope thing whatever the option says.
        if (!book && meta.ContainsKey("toc"))
        {
            meta["toc"] = PandocAst.MetaBool(false);
        }

        return meta;
    }

    // Everything the output depends on: the AST, the package's files, trim and options, the placed renditions and
    // the pandoc and Tectonic versions.
    private static string Hash(
        JsonObject ast, TemplatePackage template, string trim, IReadOnlyDictionary<string, JsonElement> options, string toolchain, IEnumerable<PlacedImage> images)
    {
        var text = new StringBuilder()
            .Append(template.Slug).Append('@').Append(template.Version).Append('#').Append(template.ContentHash).Append('|')
            .Append(toolchain).Append('|').Append(trim).Append('|')
            .AppendJoin('|', options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value.GetRawText())).Append('|')
            .AppendJoin('|', images.OrderBy(i => i.AssetId).Select(i => i.Rendition.StorageKey + ":" + i.Rendition.WidthPx + "x" + i.Rendition.HeightPx))
            .Append('|').Append(ast.ToJsonString());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
