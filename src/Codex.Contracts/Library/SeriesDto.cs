namespace Codex.Contracts.Library;

// A series' own fields (the row, without roll-ups; GET /api/library carries those).
public sealed record SeriesDto(Guid Id, string Name, string? Description);
