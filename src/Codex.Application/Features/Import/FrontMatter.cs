using System.Globalization;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// The YAML front matter the Markdown layout uses: scalars (bare or quoted), true/false, inline lists ([a, "b"]) and
// block lists ("- a" lines under the key). A block-list item that starts "name:", "title:", "path:", "url:" or "id:" is
// a small mapping (the item's own line and the indented "key: value" lines after it), read with Objects. Anything else
// is kept as its raw text.
public sealed partial class FrontMatter
{
    private readonly Dictionary<string, string> _scalars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Dictionary<string, string>>> _objects = new(StringComparer.OrdinalIgnoreCase);

    private FrontMatter()
    {
    }

    public static FrontMatter Empty { get; } = new();

    // Splits "---\n…\n---\n" off the top of text (LF line ends). No front matter: Empty and the whole text.
    public static (FrontMatter FrontMatter, string Body) Split(string text)
    {
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (Empty, text);
        }
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return (Empty, text);
        }
        var afterFence = text.IndexOf('\n', end + 1);
        var body = afterFence < 0 ? "" : text[(afterFence + 1)..];
        return (Parse(text[4..(end + 1)]), body);
    }

    public string? Scalar(string key) => _scalars.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    public bool Flag(string key) => string.Equals(Scalar(key), "true", StringComparison.OrdinalIgnoreCase);

    public int? Number(string key) => int.TryParse(Scalar(key), out var n) ? n : null;

    public IReadOnlyList<string> List(string key) => _lists.TryGetValue(key, out var list) ? list : [];

    // Each mapping item carries this key: how many plain items of its list come before it, to merge the two in order.
    public const string PlainBefore = "#before";

    // The mapping items of a block list, in order (keys are case-insensitive); plain items are in List.
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Objects(string key) =>
        _objects.TryGetValue(key, out var list) ? list : [];

    private static FrontMatter Parse(string yaml)
    {
        var result = new FrontMatter();
        string? listKey = null;
        Dictionary<string, string>? current = null;
        foreach (var line in yaml.Split('\n'))
        {
            var item = ListItem().Match(line);
            if (listKey != null && item.Success)
            {
                var start = ObjectStart().Match(item.Groups[1].Value);
                if (start.Success)
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [start.Groups[1].Value] = Unquote(start.Groups[2].Value),
                        [PlainBefore] = result._lists[listKey].Count.ToString(CultureInfo.InvariantCulture),
                    };
                    if (!result._objects.TryGetValue(listKey, out var objects))
                    {
                        result._objects[listKey] = objects = [];
                    }
                    objects.Add(current);
                }
                else
                {
                    current = null;
                    result._lists[listKey].Add(Unquote(item.Groups[1].Value));
                }
                continue;
            }
            if (listKey != null && current != null && Continuation().Match(line) is { Success: true } more)
            {
                current[more.Groups[1].Value] = Unquote(more.Groups[2].Value);
                continue;
            }
            listKey = null;
            current = null;
            var pair = KeyValue().Match(line);
            if (!pair.Success)
            {
                continue;
            }
            var key = pair.Groups[1].Value;
            var value = pair.Groups[2].Value.Trim();
            if (value.Length == 0)
            {
                listKey = key;
                result._lists[key] = [];
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                result._lists[key] = value[1..^1].Split(',').Select(v => Unquote(v.Trim())).Where(v => v.Length > 0).ToList();
            }
            else
            {
                result._scalars[key] = Unquote(value);
            }
        }
        return result;
    }

    private static string Unquote(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            return v[1..^1];
        }
        return v;
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_-]*):(.*)$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^(name|title|path|url|id):\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectStart();

    [GeneratedRegex(@"^\s+([A-Za-z_][A-Za-z0-9_-]*):\s*(.*)$")]
    private static partial Regex Continuation();

    [GeneratedRegex(@"^\s*-\s+(.*)$")]
    private static partial Regex ListItem();
}
