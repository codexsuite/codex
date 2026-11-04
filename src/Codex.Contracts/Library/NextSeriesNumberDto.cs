namespace Codex.Contracts.Library;

// The number a new book in the series would get: one above the highest, archived books included.
public sealed record NextSeriesNumberDto(Guid SeriesId, int NextNumber);
