namespace Codex.Contracts.Print;

// GET /api/books/{id}/exports: the book's exports, newest first (at most 30). Running is the id of the print export
// that is queued or running now, if any: one at a time per book.
public sealed record ExportListDto(Guid? Running, IReadOnlyList<ExportDto> Exports);
