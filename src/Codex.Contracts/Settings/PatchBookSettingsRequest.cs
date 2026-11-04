namespace Codex.Contracts.Settings;

// PATCH /api/books/{id}/settings. A missing (null) field is left alone. Empty AuthorName, StyleGuide, Subtitle or Description clears it;
// TargetWords 0 clears the target. Blank Title, an unknown Status or a negative target is a 400.
public sealed record PatchBookSettingsRequest(string? Title, string? AuthorName, string? Status, int? TargetWords, string? StyleGuide, string? Subtitle = null, string? Description = null);
