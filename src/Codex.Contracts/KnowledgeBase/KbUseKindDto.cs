namespace Codex.Contracts.KnowledgeBase;

// How a scene uses an entry. Kind is "mention", "summary", "linked", "beat", "beat_context", "timeline",
// "printed" or "text" (the name appears as plain text). Count is the occurrences.
public sealed record KbUseKindDto(string Kind, int Count);
