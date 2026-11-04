namespace Codex.Contracts.KnowledgeBase;

// Every member is optional; a member left out is not changed. Colour is one of the category tokens (CreateKbCategoryRequest).
public sealed record PatchKbCategoryRequest(string? Name, string? Colour);
