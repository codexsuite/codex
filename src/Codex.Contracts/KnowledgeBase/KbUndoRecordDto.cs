namespace Codex.Contracts.KnowledgeBase;

// Everything a delete changed, returned by DELETE /api/kb/entries/{id} and sent back to POST .../restore. The server
// keeps nothing: restore re-applies these values, and answers 409 when a scene, summary, beat or entry text changed
// since. Timeline notes need no record: their lines are in the saved scene bodies and come back with them.
public sealed record KbUndoRecordDto(
    Guid EntryId,
    string Name,
    IReadOnlyList<KbUndoSceneDto> Scenes,
    IReadOnlyList<KbUndoSummaryDto> Summaries,
    IReadOnlyList<KbUndoBeatDto> Beats,
    IReadOnlyList<KbUndoEntryTextDto> Entries,
    IReadOnlyList<Guid> Related,
    IReadOnlyList<Guid> SceneLinks,
    IReadOnlyList<Guid> BeatContexts);
