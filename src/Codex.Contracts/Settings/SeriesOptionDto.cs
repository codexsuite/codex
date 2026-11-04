namespace Codex.Contracts.Settings;

// A series the book can be moved into: its id, name and live book count.
public sealed record SeriesOptionDto(Guid Id, string Name, int BookCount);
