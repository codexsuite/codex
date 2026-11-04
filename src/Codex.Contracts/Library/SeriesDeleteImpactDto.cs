namespace Codex.Contracts.Library;

// What deleting a series does: its books become standalone; its Knowledge base entries and labels are copied onto
// each of them first. Categories are the series' own, with their live entry counts.
public sealed record SeriesDeleteImpactDto(
    Guid SeriesId,
    string Name,
    int Books,
    IReadOnlyList<SeriesDeleteCategoryDto> Categories,
    IReadOnlyList<string> Labels);
