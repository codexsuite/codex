namespace Codex.Contracts.Settings;

// The copy made by POST /api/books/{id}/duplicate (references are not copied).
public sealed record DuplicatedBookDto(Guid Id, string Title);
