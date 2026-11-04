namespace Codex.Contracts.Structure;

// POST /api/books/{id}/labels (the id may be a series, to make a series-wide label).
public sealed record CreateLabelRequest(string Name, string? Color);
