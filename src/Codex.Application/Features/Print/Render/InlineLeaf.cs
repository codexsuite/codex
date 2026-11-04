namespace Codex.Application.Features.Print.Render;

// One run of inline content with the marks on it, outermost first. Id is the mention's entry or the marker's use; a RawLatex leaf holds its source in Text.
public sealed record InlineLeaf(InlineLeafKind Kind, string Text, IReadOnlyList<InlineMark> Marks, Guid? Id = null);
