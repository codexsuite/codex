namespace Codex.Contracts.KnowledgeBase;

// One entry's whole window in a single response. BookId is the owning book or series. Fields holds a value per
// FieldDefs key. UsedIn is in book order, Timeline in book order. PositionInCategory is 1-based among the live
// entries of the category's row; CreatedLabel and UpdatedLabel are server-formatted relative labels.
public sealed record KbEntryDto(
    Guid Id,
    Guid BookId,
    Guid CategoryId,
    string CategoryName,
    string? CategoryColour,
    IReadOnlyList<KbFieldDefDto> FieldDefs,
    string Name,
    string Slug,
    string Summary,
    string Body,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<KbCustomFieldDto> CustomFields,
    IReadOnlyList<string> Aliases,
    bool AlwaysInContext,
    IReadOnlyList<string> Tags,
    bool ExcludeFromAutoContext,
    int PositionInCategory,
    int CategoryEntryCount,
    KbSourceDto? Source,
    IReadOnlyList<KbRelatedDto> Related,
    IReadOnlyList<KbUseDto> UsedIn,
    IReadOnlyList<KbTimelineNoteDto> Timeline,
    string CreatedLabel,
    string UpdatedLabel);
