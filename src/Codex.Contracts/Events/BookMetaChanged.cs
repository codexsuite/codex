namespace Codex.Contracts.Events;

// Book-level fields changed (title, status, target, cover, ...). Fields are the camelCase DTO property names.
public sealed record BookMetaChanged(Guid BookId, IReadOnlyList<string> Fields) : BookEvent(BookId);
