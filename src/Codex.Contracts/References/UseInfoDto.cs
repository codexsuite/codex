namespace Codex.Contracts.References;

// One reference_use row, archived or not: the book, source and scene it belongs to, its kind and locator. Archived means
// its marker is no longer in the prose; Pending means the marker is only in a pending AI draft.
public sealed record UseInfoDto(Guid Id, Guid BookId, Guid SourceId, Guid SceneId, string Kind, string? Locator, bool Archived, bool Pending);
