namespace Codex.Contracts.KnowledgeBase;

// POST /api/kb/entries/{id}/related: the other entry to link.
public sealed record AddKbRelatedRequest(Guid EntryId);
