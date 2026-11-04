namespace Codex.Contracts.References;

// The editable fields as sent: see SourceDetailsDto. Empty text for an optional field clears it.
public sealed record SourceDetailsRequest(
    string Kind,
    string Title,
    string AuthorsText,
    int? Year,
    string? Publisher,
    string? Edition,
    string? Isbn,
    string? Doi,
    string? Url,
    string? AccessedOn,
    string Licence,
    string? RightsHolder);
