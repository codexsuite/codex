namespace Codex.Contracts.Library;

// The series' live books with their numbers after a reorder (1..n in request order).
public sealed record SeriesOrderDto(Guid SeriesId, IReadOnlyList<SeriesOrderBookDto> Books);
