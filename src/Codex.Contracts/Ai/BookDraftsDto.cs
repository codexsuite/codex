namespace Codex.Contracts.Ai;

// GET /api/books/{id}/drafts: the pending draft of every live beat (and every scene's continuation) in the book.
public sealed record BookDraftsDto(Guid BookId, IReadOnlyList<DraftDto> Drafts);
