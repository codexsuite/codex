namespace Codex.Contracts.Structure;

// POST /api/books/{id}/acts: Title is optional (an empty title is "Name this act"). The act goes after AfterId, which
// must be a live act of the book; null appends it to the end. It starts with one chapter holding one empty scene.
public sealed record CreateActRequest(string? Title, Guid? AfterId);
