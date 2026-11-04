namespace Codex.Application.Features.Print.Render;

// Something preflight and the UI should show about a render (issue #13): Kind is one of RenderFlag's constants,
// SceneId where it sits, Detail what it is (the first line of a raw-LaTeX block, the unknown line, the missing id),
// Kept whether it reached the output (a raw-LaTeX block is kept only by a template that allows it).
public sealed record RenderFlag(string Kind, Guid? SceneId, string Detail, bool Kept)
{
    public const string RawLatex = "raw_latex";
    public const string UnknownDirective = "unknown_directive";
    public const string HiddenByComment = "hidden_by_comment";
    public const string MissingAsset = "missing_asset";
    public const string MissingEntry = "missing_entry";
    public const string MissingReference = "missing_reference";
}
