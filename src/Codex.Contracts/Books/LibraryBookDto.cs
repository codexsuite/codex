namespace Codex.Contracts.Books;

// One book on the Library shelf or list. WordCount and ProgressPct come from book_stats; LastEditedAt is the
// server's ISO timestamp and LastEditedLabel the server-formatted relative label ("2h ago"), so the SPA never
// builds dates. Status is the book.status value (planning, draft, writing, revising, published). CoverPalette is
// the design token for the generated cover; CoverInitials its letters. LastScreen is a hint (plan, write, ...)
// when the server knows one; the SPA's codex.last.<book> wins. AuthorName is the author as printed (null when
// unset), drawn at the foot of the cover. CoverUrl is the uploaded cover image, as on BookHeaderDto; null means
// the generated cover. EditedSincePublication is true for a published book whose manuscript changed after its newest
// snapshot (Settings -> Published). Role is the caller's effective role on the book (BookRoles wire value: "view",
// "comment", "write" or "owner"; #80), Owner who owns it, for the "Shared by …" line and the role chip.
public sealed record LibraryBookDto(
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
    decimal? ProgressPct,
    DateTimeOffset LastEditedAt,
    string LastEditedLabel,
    string? LastScreen,
    bool Archived,
    string? CoverUrl,
    bool EditedSincePublication,
    string Role,
    BookOwnerDto Owner);
