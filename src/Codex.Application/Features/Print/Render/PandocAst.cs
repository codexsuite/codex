using System.Text.Json.Nodes;

namespace Codex.Application.Features.Print.Render;

// Builders for pandoc's JSON AST (pandoc-types 1.23, pandoc 3.x), the form `pandoc -f json` reads. Pure.
public static class PandocAst
{
    public static readonly int[] ApiVersion = [1, 23, 1];

    public static JsonObject Document(JsonObject meta, JsonArray blocks) => new()
    {
        ["pandoc-api-version"] = new JsonArray(ApiVersion.Select(v => (JsonNode)v).ToArray()),
        ["meta"] = meta,
        ["blocks"] = blocks,
    };

    public static JsonObject Node(string type, JsonNode? content = null)
    {
        var node = new JsonObject { ["t"] = type };
        if (content is not null)
        {
            node["c"] = content;
        }

        return node;
    }

    public static JsonArray Attr(string id = "", IEnumerable<string>? classes = null, IEnumerable<(string Key, string Value)>? attributes = null) =>
    [
        id,
        new JsonArray((classes ?? []).Select(c => (JsonNode)c).ToArray()),
        new JsonArray((attributes ?? []).Select(a => (JsonNode)new JsonArray(a.Key, a.Value)).ToArray()),
    ];

    public static JsonArray List(IEnumerable<JsonNode> nodes) => new(nodes.ToArray());

    // Plain text as Str / Space / SoftBreak inlines.
    public static IEnumerable<JsonNode> Text(string text)
    {
        var word = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n')
            {
                if (word.Length > 0)
                {
                    yield return Node("Str", word.ToString());
                    word.Clear();
                }

                yield return Node(c == '\n' ? "SoftBreak" : "Space");
            }
            else
            {
                word.Append(c);
            }
        }

        if (word.Length > 0)
        {
            yield return Node("Str", word.ToString());
        }
    }

    public static JsonObject Para(IEnumerable<JsonNode> inlines) => Node("Para", List(inlines));

    public static JsonObject Plain(IEnumerable<JsonNode> inlines) => Node("Plain", List(inlines));

    public static JsonObject Header(int level, JsonArray attr, IEnumerable<JsonNode> inlines) => Node("Header", new JsonArray(level, attr, List(inlines)));

    public static JsonObject Div(JsonArray attr, IEnumerable<JsonNode> blocks) => Node("Div", new JsonArray(attr, List(blocks)));

    public static JsonObject Span(JsonArray attr, IEnumerable<JsonNode> inlines) => Node("Span", new JsonArray(attr, List(inlines)));

    public static JsonObject RawBlock(string format, string text) => Node("RawBlock", new JsonArray(format, text));

    public static JsonObject CodeBlock(JsonArray attr, string text) => Node("CodeBlock", new JsonArray(attr, text));

    public static JsonObject Code(string text) => Node("Code", new JsonArray(Attr(), text));

    public static JsonObject Link(JsonArray attr, IEnumerable<JsonNode> inlines, string target) => Node("Link", new JsonArray(attr, List(inlines), new JsonArray(target, "")));

    public static JsonObject Image(JsonArray attr, IEnumerable<JsonNode> alt, string source) => Node("Image", new JsonArray(attr, List(alt), new JsonArray(source, "")));

    public static JsonObject Figure(JsonArray attr, IEnumerable<JsonNode> caption, IEnumerable<JsonNode> blocks)
    {
        var captionInlines = List(caption);
        var captionBlocks = captionInlines.Count == 0 ? new JsonArray() : new JsonArray(Plain(captionInlines.Select(n => n!.DeepClone())));
        return Node("Figure", new JsonArray(attr, new JsonArray(null, captionBlocks), List(blocks)));
    }

    public static JsonObject BlockQuote(IEnumerable<JsonNode> blocks) => Node("BlockQuote", List(blocks));

    public static JsonObject BulletList(IEnumerable<IEnumerable<JsonNode>> items) => Node("BulletList", List(items.Select(i => (JsonNode)List(i))));

    public static JsonObject OrderedList(int start, IEnumerable<IEnumerable<JsonNode>> items) => Node("OrderedList", new JsonArray(
        new JsonArray(start, Node("Decimal"), Node("Period")),
        List(items.Select(i => (JsonNode)List(i)))));

    public static JsonObject DefinitionList(IEnumerable<(IEnumerable<JsonNode> Term, IEnumerable<JsonNode> Definition)> items) =>
        Node("DefinitionList", List(items.Select(i => (JsonNode)new JsonArray(List(i.Term), new JsonArray(List(i.Definition))))));

    public static JsonObject Note(IEnumerable<JsonNode> blocks) => Node("Note", List(blocks));

    public static JsonObject HorizontalRule() => Node("HorizontalRule");

    public static JsonObject RawInline(string format, string text) => Node("RawInline", new JsonArray(format, text));

    public static JsonObject Strikeout(IEnumerable<JsonNode> inlines) => Node("Strikeout", List(inlines));

    // A table with one header row and the body rows; each row is a list of cells, each cell a list of blocks. `alignments`
    // holds "left", "center", "right" or null per column. No caption and no foot, as a pipe table has none.
    public static JsonObject Table(IReadOnlyList<string?> alignments, IEnumerable<IEnumerable<JsonNode>> header, IEnumerable<IEnumerable<IEnumerable<JsonNode>>> body)
    {
        JsonArray Row(IEnumerable<IEnumerable<JsonNode>> cells) => new(Attr(), List(cells.Select((cell, column) =>
            (JsonNode)new JsonArray(Attr(), Node(AlignmentName(column < alignments.Count ? alignments[column] : null)), 1, 1, List(cell)))));

        return Node("Table", new JsonArray(
            Attr(),
            new JsonArray(null, new JsonArray()),
            List(alignments.Select(a => (JsonNode)new JsonArray(Node(AlignmentName(a)), Node("ColWidthDefault")))),
            new JsonArray(Attr(), new JsonArray(Row(header))),
            new JsonArray(new JsonArray(Attr(), 0, new JsonArray(), List(body.Select(r => (JsonNode)Row(r))))),
            new JsonArray(Attr(), new JsonArray())));
    }

    private static string AlignmentName(string? alignment) => alignment switch
    {
        "left" => "AlignLeft",
        "center" => "AlignCenter",
        "right" => "AlignRight",
        _ => "AlignDefault",
    };

    public static JsonObject LineBreak() => Node("LineBreak");

    public static JsonObject Strong(IEnumerable<JsonNode> inlines) => Node("Strong", List(inlines));

    public static JsonObject Emph(IEnumerable<JsonNode> inlines) => Node("Emph", List(inlines));

    public static JsonObject MetaString(string value) => Node("MetaString", value);

    public static JsonObject MetaBool(bool value) => Node("MetaBool", value);

    public static JsonObject MetaInlines(string text) => Node("MetaInlines", List(Text(text)));

    public static JsonObject MetaMap(JsonObject values) => Node("MetaMap", values);
}
