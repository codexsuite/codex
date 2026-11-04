namespace Codex.Contracts.KnowledgeBase;

// POST /api/kb/entries/{id}/make-mention: the scene whose first plain use of the entry's name (or an alias) becomes a mention.
public sealed record MakeKbMentionRequest(Guid SceneId);
