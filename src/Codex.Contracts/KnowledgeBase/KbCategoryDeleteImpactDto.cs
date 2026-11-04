namespace Codex.Contracts.KnowledgeBase;

// Live entries still in a category; a category can only be deleted when this is 0.
public sealed record KbCategoryDeleteImpactDto(int Entries);
