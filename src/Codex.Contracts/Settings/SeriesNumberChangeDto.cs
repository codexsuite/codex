namespace Codex.Contracts.Settings;

// The answer to a number change. Undo is PUT series-number with PreviousNumber. Books lists every live book, in
// the new order.
public sealed record SeriesNumberChangeDto(Guid BookId, Guid SeriesId, int Number, int PreviousNumber, IReadOnlyList<SeriesNumberEntryDto> Books);
