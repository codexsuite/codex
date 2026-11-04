namespace Codex.Contracts.Structure;

// GET /api/books/{id}/labels: the labels a book can use (its own plus its series'). ScopeId is the book or series
// that owns the label; SceneCount is how many scenes carry it.
public sealed record LabelDto(Guid Id, Guid ScopeId, string Name, string? Color, int SceneCount);
