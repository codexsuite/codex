namespace Codex.Contracts.Settings;

// A live book of the series after a number change, with the number it had before.
public sealed record SeriesNumberEntryDto(Guid Id, int Number, int PreviousNumber);
