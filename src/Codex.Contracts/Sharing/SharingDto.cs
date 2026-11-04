namespace Codex.Contracts.Sharing;

// GET /api/books/{id}/sharing (#80): the owner, the grants on this book, and the grants on its series (null for a
// standalone book). Role is the caller's own effective role on the book; only "owner" may change anything.
public sealed record SharingDto(
    SharingUserDto Owner,
    string Role,
    IReadOnlyList<SharingGrantDto> Book,
    SharingSeriesDto? Series);
