namespace Codex.Contracts.Settings;

// PATCH /api/books/{id}/publication-settings. A missing (null) field is left alone. A value equal to the series' value
// goes back to inheriting. Style outside chicago/apa/harvard, a limit of 0 or less, a year outside 1900..2200 (0
// inherits), an unknown format or a negative print run (0 inherits) is a 400. Publisher "" = publish it yourself;
// Formats [] = none.
public sealed record PatchPublicationSettingsRequest(
    string? Style,
    int? QuoteWordLimit,
    string? Publisher,
    int? PlannedYear,
    IReadOnlyList<string>? Formats,
    int? PrintRun,
    string? Territory);
