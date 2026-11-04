namespace Codex.Application.Features.Print.Render;

// Every live chapter of the book in reading order, so chapter links resolve outside the scope too. Number counts the
// numbered chapters from 1 (null for front matter); InScope marks the chapters whose scenes are rendered.
public sealed record RenderChapter(
    Guid Id, Guid ActId, string Title, bool IsNumbered, int? Number, Guid? OpenerAssetId, string? OpenerCaption, bool InScope);
