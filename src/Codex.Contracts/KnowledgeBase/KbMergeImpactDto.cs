namespace Codex.Contracts.KnowledgeBase;

// What merging an entry into another moves: scene links the target does not have yet, timeline notes (additions),
// related links the target does not have yet, and the names that become aliases of the target.
public sealed record KbMergeImpactDto(string IntoName, int SceneLinks, int Additions, int Related, IReadOnlyList<string> NewAliases);
