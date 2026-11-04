namespace Codex.Contracts.Settings;

// GET /api/books/{id}/settings (and the PATCH answer): the Book, Series and Writing sections in one response.
// ProgressPercent is words / target as a whole percent (null without a target; may pass 100). SeriesOptions are all live
// series; SeriesOrder is the live books of the book's series by number (empty when standalone). StyleGuide is the book's
// own guide; SeriesGuide is the series' guide (null when standalone or the series has none). Both are sent with every AI draft. Archived books are hidden from Library until restored.
public sealed record BookSettingsDto(
    Guid Id,
    string Title,
    string? AuthorName,
    string Status,
    int? TargetWords,
    int WordCount,
    int? ProgressPercent,
    Guid? SeriesId,
    string? SeriesName,
    int? SeriesNumber,
    IReadOnlyList<SeriesOptionDto> SeriesOptions,
    IReadOnlyList<SeriesOrderEntryDto> SeriesOrder,
    string? StyleGuide,
    string? SeriesGuide,
    bool Archived,
    string? Subtitle,
    string? Description);
