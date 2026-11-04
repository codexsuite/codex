namespace Codex.Contracts.Print;

// One version of a layout template, as fork and upload answer: the template (Slug is unique among the user's templates
// and the built-ins), the version and its changelog. ForkedFrom reads "Fantasy RPG (5e style) v1.0.0" for a fork.
public sealed record LayoutTemplateVersionDto(
    Guid TemplateId,
    Guid VersionId,
    string Slug,
    string Name,
    string Version,
    bool IsUserTemplate,
    string? ForkedFrom,
    IReadOnlyList<string> Changelog);
