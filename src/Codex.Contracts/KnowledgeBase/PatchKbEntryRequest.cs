namespace Codex.Contracts.KnowledgeBase;

// Every member is optional; a member left out is not changed. Aliases, Tags, Fields and CustomFields replace the whole set.
// A rename keeps the old name as an alias unless KeepOldName is false. A new name or alias that another entry in scope
// already answers to is refused (409 conflict) unless AllowDuplicate is true.
public sealed record PatchKbEntryRequest(
    string? Name,
    string? Summary,
    string? Body,
    IReadOnlyDictionary<string, string>? Fields,
    IReadOnlyList<KbCustomFieldDto>? CustomFields,
    IReadOnlyList<string>? Aliases,
    bool? AlwaysInContext,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags = null,
    bool? ExcludeFromAutoContext = null,
    bool? KeepOldName = null,
    bool? AllowDuplicate = null);
