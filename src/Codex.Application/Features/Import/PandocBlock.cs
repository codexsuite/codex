namespace Codex.Application.Features.Import;

// One block of a pandoc chapter file as PandocChapterReader maps it; which fields are set depends on Kind.
public sealed record PandocBlock(
    PandocBlockKind Kind,
    string? Title = null,
    string? Text = null,
    string? Image = null,
    string? Caption = null,
    string? Placement = null,
    string? Container = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    IReadOnlyList<PandocBlock>? Children = null);
