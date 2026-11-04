namespace Codex.Contracts.Library;

// Title is required. A book in a series may omit SeriesNumber (the server takes the next free one); a number
// without a SeriesId is rejected. Status defaults to "planning".
public sealed record CreateBookRequest(
    string? Title,
    string? Subtitle,
    Guid? SeriesId,
    int? SeriesNumber,
    int? TargetWords,
    string? Status);
