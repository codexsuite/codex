namespace Codex.Contracts.KnowledgeBase;

// One live entry visible to the book. CategoryId is a KbCategoryDto.Id of the same response.
// SceneCount is the live scenes of this book that use the entry; UseCount is every occurrence in them.
// SeriesName is set exactly when FromSeries is true. SummaryLine is the first non-empty line of the entry's summary,
// cut to 140 characters with "…"; "" when the entry has no summary.
public sealed record KbEntrySummaryDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    IReadOnlyList<string> Aliases,
    int SceneCount,
    int UseCount,
    bool FromSeries,
    string? SeriesName,
    bool AlwaysInContext,
    string SummaryLine);
