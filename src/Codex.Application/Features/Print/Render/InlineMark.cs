namespace Codex.Application.Features.Print.Render;

// A mark on an inline leaf; Id is the chapter (ChapterLink) or the reference use (Ref), Href the target of a Link.
public sealed record InlineMark(InlineMarkKind Kind, Guid? Id = null, string? Href = null);
