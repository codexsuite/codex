namespace Codex.Contracts.KnowledgeBase;

// Merge: when the series already has an entry with this name, fold this one into it instead of answering
// needs_confirmation.
public sealed record PromoteKbEntryRequest(bool Merge = false);
