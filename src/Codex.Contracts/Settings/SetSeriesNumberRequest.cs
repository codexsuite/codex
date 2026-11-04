namespace Codex.Contracts.Settings;

// PUT /api/books/{id}/series-number: the book's new place (1..number of live books) in its own series.
public sealed record SetSeriesNumberRequest(int Number);
