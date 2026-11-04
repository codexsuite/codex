namespace Codex.Contracts.Print;

// A print issue on the preview: Kind is raw_latex, unknown_directive, missing_asset, missing_entry or missing_reference;
// Kept is whether a raw-LaTeX block reaches the print.
public sealed record LayoutFlagDto(string Kind, Guid? SceneId, string Detail, bool Kept);
