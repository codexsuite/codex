using System.Text.RegularExpressions;
using Codex.Application.Prose;

namespace Codex.Application.Mcp;

// The stored-format rules for prose an agent writes (edit_prose edits, submit_draft text): the agent may not invent
// ::beat, ::kb-addition or ::figure lines or ref: source links. Each one points at a row that only its own tool creates
// (upsert_beat, link_kb_entry, place_figure, cite_source), and the database triggers bring an archived row back to life when an id
// appears in a body, so the guard cannot rely on the C# parser agreeing with them. It therefore reads the text the way
// the triggers do (db/schema/0002_views_triggers.sql: the directive regex and the ref: link regex, matched over the
// whole text, fences and quotes included) and, more strictly, finds a directive id anywhere in the text, so a line
// the TS parser would draw as a card inside a quote or list is caught too. An id is allowed only if the text it
// replaces already had it; mentions ([Name](kb:id)) and plain Markdown pass.
public static partial class McpProseRules
{
    public static void RefuseInvented(string field, string before, string after)
    {
        var kind = InventedKind(before, after);
        if (kind == null)
        {
            return;
        }
        throw McpToolException.Invalid(field, kind switch
        {
            "beat" => "Prose may not add a ::beat line; use upsert_beat.",
            "kb-addition" => "Prose may not add a ::kb-addition line; use link_kb_entry.",
            "figure" => "Prose may not add a ::figure line; use place_figure.",
            _ => "Prose may not add a ref: link; use cite_source.",
        });
    }

    // The kind ("beat", "kb-addition", "figure" or "ref") of the first id `after` carries that `before` did not, or null.
    // Removing or keeping an id is never invented.
    private static string? InventedKind(string before, string after)
    {
        var known = Ids(before);
        return Ids(after).Where(i => !known.Contains(i)).Select(i => i.Kind).FirstOrDefault();
    }

    // What the body would hand the database: directive ids (anywhere) and ref: link ids (the trigger's regex over the
    // whole body), plus whatever the C# parser sees as an embed.
    // Shared with the web editor's guard (EditorProseRules).
    public static IReadOnlyList<(string Kind, Guid Id)> IdsIn(string body) => Ids(body).ToList();

    private static HashSet<(string Kind, Guid Id)> Ids(string body)
    {
        var ids = new HashSet<(string, Guid)>();
        foreach (var groups in Directive().Matches(body).Select(m => m.Groups))
        {
            if (Guid.TryParse(groups["id"].Value, out var id))
            {
                ids.Add((groups["kind"].Value, id));
            }
        }
        foreach (var (id, _) in Refs(body))
        {
            ids.Add(("ref", id));
        }
        foreach (var e in ProseDocument.Parse(body).ListEmbeds())
        {
            var kind = e.Kind switch
            {
                ProseEmbedKind.Beat => "beat",
                ProseEmbedKind.KbAddition => "kb-addition",
                ProseEmbedKind.Figure => "figure",
                ProseEmbedKind.Ref => "ref",
                _ => null,
            };
            if (kind != null)
            {
                ids.Add((kind, e.Id));
            }
        }
        return ids;
    }

    // Prose passages inside ref: links, which feed clearance: the passages of ref links the text has that the old text did not.
    public static IReadOnlyList<Guid> RefsTouched(string before, string after)
    {
        var old = Refs(before).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Text);
        return Refs(after).Where(r => !old.TryGetValue(r.Id, out var was) || was != r.Text).Select(r => r.Id).Distinct().ToList();
    }

    private static IEnumerable<(Guid Id, string Text)> Refs(string body) =>
        RefLink().Matches(body).Where(m => Guid.TryParse(m.Groups["id"].Value, out _))
            .Select(m => (Guid.Parse(m.Groups["id"].Value), m.Groups["text"].Value));

    // Same ids the trigger reads, whitespace-tolerant and unanchored.
    [GeneratedRegex(@"::(?<kind>beat|kb-addition|figure)\{id=""(?<id>[0-9a-fA-F-]{36})""\}")]
    private static partial Regex Directive();

    [GeneratedRegex(@"\[(?<text>(?:[^\[\]\\]|\\.)*)\]\(ref:(?<id>[0-9a-fA-F-]{36})\)", RegexOptions.Singleline)]
    private static partial Regex RefLink();
}
