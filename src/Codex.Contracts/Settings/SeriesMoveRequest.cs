namespace Codex.Contracts.Settings;

// POST /api/books/{id}/series-move and /series-move/preview. SeriesId null = leave the series (standalone).
public sealed record SeriesMoveRequest(Guid? SeriesId);
