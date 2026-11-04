namespace Codex.Contracts.Library;

// The book as stored. The default act (position a0) is made by the database.
public sealed record CreatedBookDto(
    Guid Id,
    string Title,
    string? Subtitle,
    Guid? SeriesId,
    int? SeriesNumber,
    string Status,
    int? TargetWords);
