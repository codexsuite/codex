using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// The parts of a pandoc defaults file (`pandoc -d book.yaml`) the import reads: input-files (or input-file), and the
// title, subtitle and author under metadata (or at the top level). Comments ("# …", also "#- skipped.md" inside the
// list) and every other key are ignored. Only the block and inline list forms pandoc's own examples use.
public sealed partial class PandocDefaults
{
    private PandocDefaults(IReadOnlyList<string> inputFiles, IReadOnlyDictionary<string, string> metadata)
    {
        InputFiles = inputFiles;
        Metadata = metadata;
    }

    public IReadOnlyList<string> InputFiles { get; }

    // title, subtitle, author: metadata.<key>, else the top-level key.
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static PandocDefaults Parse(string yaml)
    {
        var inputs = new List<string>();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var topLevel = new Dictionary<string, string>(StringComparer.Ordinal);
        string? section = null;
        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }
            var indented = line.Length > trimmed.Length;
            if (!indented && KeyValue().Match(line) is { Success: true } top)
            {
                section = top.Groups[1].Value;
                var value = top.Groups[2].Value.Trim();
                if (section is "input-files" && value.StartsWith('[') && value.EndsWith(']'))
                {
                    inputs.AddRange(value[1..^1].Split(',').Select(Unquote).Where(v => v.Length > 0));
                }
                else if (section is "input-file" && value.Length > 0)
                {
                    inputs.Add(Unquote(value));
                }
                else if (value.Length > 0)
                {
                    topLevel[section] = Unquote(value);
                }
                continue;
            }
            if (section == "input-files" && ListItem().Match(line) is { Success: true } item)
            {
                inputs.Add(Unquote(item.Groups[1].Value));
            }
            else if (section == "metadata" && indented && KeyValue().Match(trimmed) is { Success: true } meta && meta.Groups[2].Value.Trim().Length > 0)
            {
                metadata[meta.Groups[1].Value] = Unquote(meta.Groups[2].Value);
            }
        }
        foreach (var key in new[] { "title", "subtitle", "author" })
        {
            if (!metadata.ContainsKey(key) && topLevel.TryGetValue(key, out var value))
            {
                metadata[key] = value;
            }
        }
        return new PandocDefaults(inputs, metadata);
    }

    private static string Unquote(string value)
    {
        var v = value.Trim();
        var comment = v.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0 && !(v.StartsWith('"') || v.StartsWith('\'')))
        {
            v = v[..comment].TrimEnd();
        }
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            return v[1..^1];
        }
        return v;
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_-]*):(.*)$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^\s*-\s+(.*)$")]
    private static partial Regex ListItem();
}
