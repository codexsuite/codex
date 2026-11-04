namespace Codex.Contracts.KnowledgeBase;

// AfterId null puts the entry first in its category.
public sealed record MoveKbEntryRequest(Guid? AfterId);
