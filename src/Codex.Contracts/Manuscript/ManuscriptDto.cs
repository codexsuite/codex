namespace Codex.Contracts.Manuscript;

// GET /api/books/{id}/manuscript: every live scene's prose in reading order, for the Write screen's one request.
// Acts, chapters and numbering come from the outline.
public sealed record ManuscriptDto(Guid BookId, IReadOnlyList<ManuscriptSceneDto> Scenes);
