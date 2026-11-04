namespace Codex.Contracts.References;

// The editable fields. AuthorsText is "Family, Given; Family, Given", a name without a comma being an organisation.
// AccessedOn is yyyy-MM-dd. Missing names the fields the kind requires that are empty. Identifiers holds the latest
// check of each ISBN, DOI and URL that still matches the field.
public sealed record SourceDetailsDto(
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
    string? RightsHolder,
    string? FairUseNote,
    IReadOnlyList<string> Missing,
    IReadOnlyList<IdentifierCheckDto> Identifiers);
