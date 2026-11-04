namespace Codex.Contracts.Settings;

// GET/PATCH /api/books/{id}/publication-settings and DELETE …/{field}: the Citations & permissions section.
// Style is chicago, apa or harvard; QuoteWordLimit is the prose words from one source quoted without permission.
// Publisher "" means "you publish it yourself" (null: not set). Formats (empty when none) are print, ebook, pdf, audio. SeriesName is
// null for a standalone book, which has no "Series" chips.
public sealed record PublicationSettingsDto(
    Guid BookId,
    string? SeriesName,
    PublicationValueDto<string> Style,
    string RulesName,
    PublicationValueDto<int> QuoteWordLimit,
    PublicationValueDto<string?> Publisher,
    PublicationValueDto<int?> PlannedYear,
    PublicationValueDto<IReadOnlyList<string>> Formats,
    PublicationValueDto<int?> PrintRun,
    PublicationValueDto<string?> Territory);
