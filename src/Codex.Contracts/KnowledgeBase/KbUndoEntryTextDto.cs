namespace Codex.Contracts.KnowledgeBase;

// Another entry's summary and body as they were before the delete.
public sealed record KbUndoEntryTextDto(Guid EntryId, string? Summary, string Body);
