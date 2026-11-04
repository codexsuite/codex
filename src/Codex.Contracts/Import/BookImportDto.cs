namespace Codex.Contracts.Import;

// GET /api/books/{id}/import: the book's import whose banner is still showing, or null. CanUndo is false once the book
// has prose written after the import (Undo import would delete that work, so it is refused as blocked).
public sealed record BookImportDto(ImportSummaryDto? Banner, bool CanUndo);
