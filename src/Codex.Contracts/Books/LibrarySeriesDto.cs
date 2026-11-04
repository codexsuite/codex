namespace Codex.Contracts.Books;

// A series stack on the shelf: its roll-up and its books in series_number order. Both cover only the live books the
// caller can open (#80): a series shared with them shows all its books, one book shared alone shows only that book.
// PartlyShared is true when the caller reaches the series only through grants on some of its books (not the owner, no
// grant on the series itself), so the shelf can say why the other books are missing. Archived is true only in the
// Archived view, for a series that is itself archived (its books are listed under it and read as archived with it;
// restoring the series brings them back).
public sealed record LibrarySeriesDto(
    Guid Id,
    string Name,
    string? Description,
    int BookCount,
    int PublishedCount,
    int WordCount,
    DateTimeOffset LastEditedAt,
    string LastEditedLabel,
    IReadOnlyList<LibraryBookDto> Books,
    bool PartlyShared,
    bool Archived);
