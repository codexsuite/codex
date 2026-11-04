namespace Codex.Contracts.Library;

// A new series from standalone books. BookIds is the picked order: it sets Book 001, 002, ... May be empty.
public sealed record CreateSeriesRequest(string? Name, IReadOnlyList<Guid>? BookIds, string? Description = null, string? AuthorName = null);
