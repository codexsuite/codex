namespace Codex.Contracts.KnowledgeBase;

// What deleting an entry touches: mention occurrences turned to plain text, scenes that use it in any way, timeline
// notes deleted, related links removed.
public sealed record KbDeleteImpactDto(int Mentions, int Scenes, int Notes, int Links);
