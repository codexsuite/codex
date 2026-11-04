namespace Codex.Contracts.KnowledgeBase;

// A sidebar category for one book: its own and its series', same-named ones merged. Id is the book's own
// category when it has one, otherwise the series'. FromSeries is true when only the series has the category
// (no same-named book category); SeriesName is set exactly then. Colour is a design token name; Position sorts ordinally.
public sealed record KbCategoryDto(
    Guid Id, string Name, string? Tag, string? Colour, string Position, IReadOnlyList<KbFieldDefDto> FieldDefs,
    bool FromSeries, string? SeriesName);
