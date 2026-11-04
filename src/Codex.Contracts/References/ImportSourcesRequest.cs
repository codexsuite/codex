namespace Codex.Contracts.References;

// POST /api/books/{id}/references/import. At most 500 items; 400 validation above that.
public sealed record ImportSourcesRequest(IReadOnlyList<ImportSourceItem> Items);
