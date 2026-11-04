using Codex.Contracts.Books;

namespace Codex.Application.Mcp;

// A resolved BookRef: a book or a series the current user can open (#80: owns, or has a grant on), with their role
// there and who owns it.
public sealed record McpBook(
    Guid Id, string Kind, string Title, Guid? SeriesId, string? SeriesTitle, int? SeriesNumber, string Status, bool Archived,
    DateTimeOffset LastEditedAt, string Role, Guid OwnerId, string OwnerName)
{
    public bool IsSeries => Kind == "series";

    public McpItem Item => new(Id, Kind, Label, IsSeries ? CodexLinks.Series(Id) : CodexLinks.Plan(Id));

    // The item a refs lookup offers as a candidate (ambiguous, or a close match): it names the owner, so a title the
    // caller's own book shares with a book shared with them can be told apart.
    public McpItem Candidate => Item with { Owner = Owner };

    public BookOwnerDto Owner => new(OwnerId, OwnerName);

    // "Hollow Throne", with the series place when it has one: "Hollow Throne (The Ashen Crown 003)".
    public string Label => SeriesTitle is { } series && SeriesNumber is { } number ? $"{Title} ({series} {number:000})" : Title;
}
