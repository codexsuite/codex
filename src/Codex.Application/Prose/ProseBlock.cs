namespace Codex.Application.Prose;

// A run of whole lines of the body. Start is the offset of the first character; End is the offset just past the
// last character of the last line, excluding that line's newline.
public sealed record ProseBlock(ProseBlockKind Kind, int Start, int End, string? Directive = null, Guid? Id = null);
