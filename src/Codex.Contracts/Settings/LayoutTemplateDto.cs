namespace Codex.Contracts.Settings;

// The template a book uses now. LatestVersion is the newest published one when it differs from PinnedVersion.
public sealed record LayoutTemplateDto(
    string Name,
    string? Description,
    bool IsUserTemplate,
    string PinnedVersion,
    string? LatestVersion,
    string? TrimSize,
    IReadOnlyList<string> Trims);
