namespace Codex.Contracts.Library;

// The series as stored, and its books in their new order.
public sealed record CreatedSeriesDto(Guid Id, string Name, IReadOnlyList<Guid> BookIds);
