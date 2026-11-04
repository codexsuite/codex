namespace Codex.Contracts.Print;

// A template the picker offers: a built-in or the user's own (IsUserTemplate), at the version this book renders it with
// (Version and VersionId: the pinned one when the book has used the template, else the newest). Notices are what the UI
// says without blocking (no preview.css). LatestVersion (and its id) is set only when a newer published version than
// Version exists; LatestChangelog then lists what changed since Version, newest first. ForkedFrom reads
// "Fantasy RPG (5e style) v1.0.0" for a fork.
public sealed record LayoutTemplateDto(
    string Slug,
    string Name,
    string Version,
    string Engine,
    IReadOnlyList<string> Trims,
    int Columns,
    bool HasPreviewCss,
    IReadOnlyList<LayoutOptionDto> Options,
    IReadOnlyList<string> Notices,
    Guid? TemplateId = null,
    Guid? VersionId = null,
    bool IsUserTemplate = false,
    string? Description = null,
    string? ForkedFrom = null,
    string? LatestVersion = null,
    Guid? LatestVersionId = null,
    IReadOnlyList<string>? LatestChangelog = null);
