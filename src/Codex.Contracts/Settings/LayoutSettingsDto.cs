namespace Codex.Contracts.Settings;

// GET /api/books/{id}/settings/layout: Layout & print, read-only. Template is null when the book has none yet.
// RiskCount is the copyright risks in References, which block print export.
public sealed record LayoutSettingsDto(LayoutTemplateDto? Template, int RiskCount);
