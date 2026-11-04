namespace Codex.Contracts.Print;

// POST /api/books/{id}/manuscript-exports: a Word, Markdown or EPUB copy of the manuscript (not for print).
// Format is docx, markdown or epub; Scope is book or chapter (ChapterId names the chapter). IncludeBeats adds each beat's
// instruction as a block quote starting "Beat:"; IncludeSummaries starts each scene with its summary in italics.
public sealed record ManuscriptExportRequest(
    string Format,
    string Scope = "book",
    Guid? ChapterId = null,
    bool IncludeBeats = false,
    bool IncludeSummaries = false);
