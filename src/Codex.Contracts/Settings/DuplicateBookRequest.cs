namespace Codex.Contracts.Settings;

// POST /api/books/{id}/duplicate. A blank or over-long (300) title is a 400.
public sealed record DuplicateBookRequest(string Title);
