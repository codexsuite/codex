namespace Codex.Contracts.References;

// GET /api/references/{id}, and the body every write on a source answers with. Number is the marker number (null when
// nothing cites the source). AuthorsLabel is the display form ("Cagan and Jones"). Checks run in the order the panel
// lists them. Permission is the permission tracker (null when no request is live or answered). QuotedWords counts toward Limit, the book's quote limit. Kinds and Licences are the pickers' options.
public sealed record SourceDetailDto(
    Guid Id,
    Guid BookId,
    int? Number,
    string Kind,
    string KindLabel,
    string Title,
    string AuthorsLabel,
    int? Year,
    string? Publisher,
    SourceVerdictDto Verdict,
    IReadOnlyList<ClearanceCheckDto> Checks,
    IReadOnlyList<SourceUseDto> Uses,
    int QuotedWords,
    int Limit,
    SourceEntryDto? Entry,
    SourceDetailsDto Details,
    IReadOnlyList<SourceOptionDto> Kinds,
    IReadOnlyList<SourceOptionDto> Licences,
    IReadOnlyList<SourceHistoryDto> History,
    PermissionDto? Permission);
