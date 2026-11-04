namespace Codex.Contracts.Settings;

// One live book in the book's series, by number. Status is the stored value ("published" is warned on renumbering).
public sealed record SeriesOrderEntryDto(Guid Id, string Title, int Number, string Status);
