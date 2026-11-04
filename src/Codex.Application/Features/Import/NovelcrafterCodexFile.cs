namespace Codex.Application.Features.Import;

// One Codex entry file out of a Novelcrafter export (characters/mara.md, other/goodharts-law.md): the KSUID the scenes
// refer to it by, its category (the folder), and its text. Tags, Type and AlwaysInContext come from the front matter.
internal sealed record NovelcrafterCodexFile(
    string Path,
    string? Id,
    string Category,
    string Name,
    IReadOnlyList<string> Aliases,
    string? Summary,
    string? Body,
    IReadOnlyList<string>? TagList = null,
    string? Type = null,
    bool AlwaysInContext = false)
{
    // Front-matter tags (Novelcrafter's own labels for the entry).
    public IReadOnlyList<string> Tags => TagList ?? [];
}
