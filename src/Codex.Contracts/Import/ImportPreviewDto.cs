namespace Codex.Contracts.Import;

// Step 2 of the Import dialog: what an import of these files would create. Source is the book_import source
// ("markdown"); nothing is written until POST /api/imports. Images are the paths (as uploaded) of the images the import
// places, for a source that takes them (pandoc): the dialog uploads exactly these with POST /api/imports.
public sealed record ImportPreviewDto(
    string Source,
    string BookTitle,
    int FileCount,
    int Words,
    IReadOnlyList<ImportCountDto> Creates,
    IReadOnlyList<ImportSkippedDto> Skipped,
    IReadOnlyList<string>? Images = null);
