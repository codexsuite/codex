namespace Codex.Contracts.KnowledgeBase;

// Colour is one of the category tokens: category-blog, -law, -story, -research, -person.
public sealed record CreateKbCategoryRequest(string? Name, string? Colour);
