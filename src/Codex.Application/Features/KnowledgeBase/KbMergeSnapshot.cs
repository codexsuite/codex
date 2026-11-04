namespace Codex.Application.Features.KnowledgeBase;

// What a merge changed, enough to put it back (KbMergeCommands.UnmergeAsync): every text that named the source before it was
// pointed at the target, the scene body versions the merge left, the links, notes and references that moved (and which links
// the target gained, since a link it already had stays on undo), and the target's aliases and body before and after. An unmerge runs only while all of it still reads what the merge left
// (409 otherwise).
public sealed record KbMergeSnapshot(
    Guid SourceId,
    Guid TargetId,
    IReadOnlyList<KbMergeSnapshot.SceneBody> Scenes,
    IReadOnlyList<KbMergeSnapshot.Text> Summaries,
    IReadOnlyList<KbMergeSnapshot.Text> Beats,
    IReadOnlyList<KbMergeSnapshot.EntryText> Entries,
    IReadOnlyList<Guid> SceneLinks,
    IReadOnlyList<Guid> SceneLinksAdded,
    IReadOnlyList<Guid> BeatContexts,
    IReadOnlyList<Guid> BeatContextsAdded,
    IReadOnlyList<Guid> Additions,
    IReadOnlyList<Guid> References,
    IReadOnlyList<Guid> Related,
    IReadOnlyList<Guid> RelatedAdded,
    IReadOnlyList<string> AliasesBefore,
    string BodyBefore,
    IReadOnlyList<string> AliasesAfter,
    string BodyAfter)
{
    // A scene body before the merge; Version is the version the merge left the scene at.
    public sealed record SceneBody(Guid SceneId, int Version, string Body);

    // A scene summary or beat body before the merge.
    public sealed record Text(Guid Id, string Value);

    // Another entry's summary and body before the merge.
    public sealed record EntryText(Guid Id, string? Summary, string Body);
}
