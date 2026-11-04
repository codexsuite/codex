namespace Codex.Contracts.Books;

// GET /api/library: series (with their books) and standalone books.
public sealed record LibraryDto(IReadOnlyList<LibrarySeriesDto> Series, IReadOnlyList<LibraryBookDto> Standalone);
