namespace Codex.Contracts.Manuscript;

// What POST .../replace/undo needs to take a replace back as one action: for each changed scene the revision that
// holds its text from before, and the old text of each changed summary and beat instruction.
public sealed record ReplaceUndoDto(
    IReadOnlyList<ReplaceUndoProse> Prose, IReadOnlyList<ReplaceUndoSummary> Summaries, IReadOnlyList<ReplaceUndoBeat> Beats);
