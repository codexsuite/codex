using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// A restored book is all new rows: every id in the backup gets a new one, and every place an old id appears (a foreign
// key in the JSON, a ::beat line or a kb:/ref:/chapter: link in the prose, a note) is rewritten to it. The ids are
// UUIDs, so one text replace serves every column. An id that is not in the backup (a series entry the book mentioned)
// is left alone.
public sealed partial class BackupIds
{
    private readonly Dictionary<string, Guid> _map = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Uuid();

    [GeneratedRegex("\\[((?:[^\\[\\]\\\\]|\\\\.)*)\\]\\((kb|chapter|ref):([0-9a-fA-F-]{36})\\)")]
    private static partial Regex Link();

    [GeneratedRegex("^::(?:(?:beat|kb-addition)\\{id=\"([0-9a-fA-F-]{36})\"\\}|(?:spell|statblock)\\{kb=\"([0-9a-fA-F-]{36})\"\\}|figure\\{id=\"[0-9a-fA-F-]{36}\"\\})[ \\t]*$", RegexOptions.Multiline)]
    private static partial Regex EmbedLine();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex BlankRuns();

    public static BackupIds From(BackupData data)
    {
        var ids = new BackupIds();
        foreach (var (file, property) in new[]
        {
            (data.Structure, "acts"), (data.Structure, "chapters"), (data.Structure, "scenes"), (data.Structure, "beats"),
            (data.KnowledgeBase, "categories"), (data.KnowledgeBase, "entries"), (data.KnowledgeBase, "additions"),
            (data.References, "sources"), (data.References, "uses"), (data.References, "permission_requests"), (data.References, "quote_flags"),
            (data.Labels, "labels"),
        })
        {
            foreach (var row in file[property] is JsonArray rows ? rows.OfType<JsonObject>() : [])
            {
                if (row["id"] is JsonValue id && id.TryGetValue<string>(out var text) && Guid.TryParse(text, out _))
                {
                    ids._map[text] = Guid.CreateVersion7();
                }
            }
        }

        return ids;
    }

    public Guid New(string oldId) => _map[oldId];

    public string Remap(string text) => Uuid().Replace(text, m => _map.TryGetValue(m.Value, out var id) ? id.ToString() : m.Value);

    // A scene body made safe to write: links to things the backup does not hold become their words, and embed lines whose
    // row it does not hold (figures: images are not in a backup) go. Ids are not rewritten yet.
    public string CleanBody(string body)
    {
        var links = Link().Replace(body, UnlessKnown);
        var lines = EmbedLine().Replace(links, KnownEmbed);
        var tidy = BlankRuns().Replace(lines, "\n\n").Trim('\n');
        return tidy.Length == 0 ? string.Empty : tidy + "\n";
    }

    // A link to a row the backup holds stays; any other is its words (a bare source marker has none).
    private string UnlessKnown(Match link)
    {
        if (_map.ContainsKey(link.Groups[3].Value))
        {
            return link.Value;
        }

        return link.Groups[1].Value == "^" ? string.Empty : link.Groups[1].Value;
    }

    private string KnownEmbed(Match line)
    {
        if (line.Value.StartsWith("::figure", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var id = line.Groups[1].Success ? line.Groups[1].Value : line.Groups[2].Value;
        return _map.ContainsKey(id) ? line.Value : string.Empty;
    }
}
