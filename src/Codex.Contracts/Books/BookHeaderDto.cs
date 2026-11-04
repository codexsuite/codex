namespace Codex.Contracts.Books;

// GET /api/books/{id}: what the book bar shows. Scope lists the books/series whose knowledge base and labels
// this book can see (book_scope). ReferenceBadgeCount and ReferenceBadgeTone are the References tab badge
// (design/README.md): the copyright risks with tone "danger" when there are any, otherwise the sources to check
// with tone "warn"; tone null when the count is 0. Both null for an archived book (no clearance summary). CoverUrl is the
// uploaded cover image (its web rendition once made, the original until then); null means the generated cover.
// Role is the caller's effective role on the book ("view", "comment", "write" or "owner"; #80), which the client's
// useBookRole() reads to go read-only below "write"; Owner is who owns the book.
public sealed record BookHeaderDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? AuthorName,
    Guid? SeriesId,
    string? SeriesName,
    int? SeriesNumber,
    string Status,
    string CoverPalette,
    string CoverInitials,
    int WordCount,
    int? TargetWords,
    IReadOnlyList<BookScopeEntryDto> Scope,
    int? ReferenceBadgeCount,
    string? ReferenceBadgeTone,
    DateTimeOffset LastEditedAt,
    string LastEditedLabel,
    string? CoverUrl,
    string Role,
    BookOwnerDto Owner);
