using System.Text.RegularExpressions;
using Codex.Application.Prose;
using Codex.Contracts.Ai;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;

namespace Codex.Application.Mcp;

// One scene body as read_manuscript returns it, block by block (ProseDocument's blocks, so paging never splits one).
// `markdown` keeps each block exactly as stored. `annotated` swaps embeds for readable text (#20 B, M2): a beat
// becomes its instruction, target and context, followed by its pending draft; a Knowledge base addition its entry and
// text; a source passage keeps its words and gains its marker number and status; mentions read @[Name] and chapter
// links §[Title]. A pending continuation draft follows the scene's last block.
public static partial class ManuscriptText
{
    public sealed record Context(
        IReadOnlyDictionary<Guid, BeatDto> Beats,
        IReadOnlyDictionary<Guid, KbAdditionDto> Additions,
        IReadOnlyDictionary<Guid, string> EntryNames,
        IReadOnlyDictionary<Guid, MarkerDto> Markers,
        IReadOnlyList<DraftDto> Drafts);

    // Start: where the stored block it came from begins in the body (markdown paging slices the body there).
    public sealed record Block(string Text, int Words, int Start);

    public static IReadOnlyList<Block> Blocks(string body, Context context, bool annotated, bool includeHiddenBeats)
    {
        var document = ProseDocument.Parse(body);
        var blocks = new List<Block>();
        var all = document.Blocks;
        for (var i = 0; i < all.Count; i++)
        {
            var raw = body[all[i].Start..all[i].End];
            if (!annotated)
            {
                blocks.Add(new Block(raw, CountWords(raw), all[i].Start));
                continue;
            }
            foreach (var text in Annotate(all, i, raw, context, includeHiddenBeats))
            {
                blocks.Add(new Block(text, CountWords(text), all[i].Start));
            }
        }
        if (annotated)
        {
            blocks.AddRange(context.Drafts.Where(d => d.BeatId == null).Select(d => new Block(Draft(d), CountWords(d.Text), body.Length)));
        }
        return blocks;
    }

    public static string Draft(DraftDto draft) =>
        $"[Pending draft · {draft.Model} · attempt {draft.Attempt}{(draft.Template is { } t ? $" · {t}" : "")} · {draft.Words} words · id {draft.Id}]\n{draft.Text.Trim()}";

    public static int Paragraphs(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    public static int CountWords(string text) => Word().Count(text);

    private static IEnumerable<string> Annotate(
        IReadOnlyList<ProseBlock> blocks, int index, string raw, Context context, bool includeHiddenBeats)
    {
        var block = blocks[index];
        if (block.Kind != ProseBlockKind.Directive || block.Id is not { } id)
        {
            yield return Inline(raw, context);
            yield break;
        }
        switch (block.Directive)
        {
            case "beat" when context.Beats.TryGetValue(id, out var beat):
                if (!beat.IsHidden || includeHiddenBeats)
                {
                    yield return BeatText(beat, HasProseUnder(blocks, index), context);
                }
                foreach (var draft in context.Drafts.Where(d => d.BeatId == id))
                {
                    yield return Draft(draft);
                }
                break;
            case "kb-addition" when context.Additions.TryGetValue(id, out var addition):
                yield return $"[Knowledge base addition · {Name(addition.EntryId, context)}] {Inline(addition.Body, context)}";
                break;
            case "figure":
                yield return $"[Figure · id {id}]";
                break;
            case "spell" or "statblock":
                yield return $"[{block.Directive} · {Name(id, context)}]";
                break;
            default:
                yield return raw;
                break;
        }
    }

    private static string BeatText(BeatDto beat, bool hasProse, Context context)
    {
        var facts = new List<string> { "Beat" };
        if (beat.TargetWordCount is { } target)
        {
            facts.Add($"{target} words");
        }
        if (beat.IsHidden)
        {
            facts.Add("hidden");
        }
        if (!hasProse)
        {
            facts.Add("no prose yet");
        }
        if (beat.ContextEntryIds.Count > 0)
        {
            facts.Add("context: " + string.Join(", ", beat.ContextEntryIds.Select(e => Name(e, context))));
        }
        return $"[{string.Join(" · ", facts)} · id {beat.Id}]\n  {Inline(beat.Body, context).Trim()}";
    }

    // Whether any text block sits between this beat line and the next beat (or the end of the scene).
    private static bool HasProseUnder(IReadOnlyList<ProseBlock> blocks, int beatIndex) =>
        blocks.Skip(beatIndex + 1)
            .TakeWhile(b => !(b.Kind == ProseBlockKind.Directive && b.Directive == "beat"))
            .Any(b => b.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Heading or ProseBlockKind.Quote or ProseBlockKind.List);

    private static string Inline(string text, Context context) => Link().Replace(text, m =>
    {
        var linkText = m.Groups["text"].Value;
        var id = Guid.Parse(m.Groups["id"].Value);
        return m.Groups["scheme"].Value switch
        {
            "kb" => $"@[{linkText}]",
            "chapter" => $"§[{linkText}]",
            _ => (linkText == "^" ? "" : linkText) + Marker(id, context),
        };
    });

    private static string Marker(Guid useId, Context context) =>
        context.Markers.TryGetValue(useId, out var marker) ? $" [#{marker.Number} · {marker.Status}]" : " [source]";

    private static string Name(Guid entryId, Context context) =>
        context.EntryNames.TryGetValue(entryId, out var name) ? name : entryId.ToString();

    [GeneratedRegex(@"\[(?<text>(?:\\.|[^\[\]\\])*)\]\((?<scheme>kb|chapter|ref):(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")]
    private static partial Regex Word();
}
