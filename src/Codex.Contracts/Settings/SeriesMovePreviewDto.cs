namespace Codex.Contracts.Settings;

// POST /api/books/{id}/series-move/preview. Nothing is written. Notes are informational; Warnings use the warn tone.
// Sharing (#80): TargetSeriesSharedWith is how many people the target series is shared with, who will be able to see the
// book once it joins ("3 people with access to The Forsaken Gods will be able to see this book."; 0 when the target is
// standalone). LosingAccess is how many people can open the book now only through its current series and will lose it.
public sealed record SeriesMovePreviewDto(
    Guid BookId,
    string Title,
    SeriesMoveEndDto From,
    SeriesMoveEndDto To,
    IReadOnlyList<SeriesMoveStepDto> Steps,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings,
    int TargetSeriesSharedWith,
    int LosingAccess);
