namespace Codex.Contracts.References;

// POST /api/books/{id}/references. Via is the identifier kind the details came from (isbn, doi, url) or null for a
// source typed in by hand; it only words the history line.
public sealed record AddSourceRequest(SourceDetailsRequest Details, string? Via);
