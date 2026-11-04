namespace Codex.Contracts.KnowledgeBase;

// A name (or alias) another entry in scope already answers to is refused (409 conflict) unless AllowDuplicate is true.
public sealed record CreateKbEntryRequest(Guid CategoryId, string? Name, bool? AllowDuplicate = null);
