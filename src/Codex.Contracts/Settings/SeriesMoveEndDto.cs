namespace Codex.Contracts.Settings;

// One end of a move: the series (null name = standalone) and the book's number there.
public sealed record SeriesMoveEndDto(Guid? Id, string? Name, int? Number);
