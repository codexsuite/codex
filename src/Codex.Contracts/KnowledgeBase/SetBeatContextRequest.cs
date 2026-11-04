namespace Codex.Contracts.KnowledgeBase;

public sealed record SetBeatContextRequest(IReadOnlyList<Guid> EntryIds);
