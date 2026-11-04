namespace Codex.Contracts.Structure;

// GET /api/books/{id}/outline: the book's acts, chapters and scenes in reading order. Archived acts are left out;
// archived chapters stay (Archived = true, Number = null) so a client can offer Restore; archived scenes are
// counted per chapter, not listed.
public sealed record OutlineDto(Guid BookId, IReadOnlyList<OutlineActDto> Acts);
