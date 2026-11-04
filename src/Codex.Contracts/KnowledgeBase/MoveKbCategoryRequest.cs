namespace Codex.Contracts.KnowledgeBase;

// AfterId null puts the category first among the categories of its book or series; otherwise it goes right after that
// category, which must belong to the same book or series.
public sealed record MoveKbCategoryRequest(Guid? AfterId);
