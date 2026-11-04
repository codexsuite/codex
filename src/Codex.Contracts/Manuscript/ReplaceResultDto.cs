namespace Codex.Contracts.Manuscript;

// Replaced counts every change; Scenes the scenes it touched. The skipped counts are matches left as they were.
public sealed record ReplaceResultDto(
    int Replaced, int Scenes, int SkippedCited, int SkippedSpansLink, int InDrafts, ReplaceUndoDto Undo);
