namespace Codex.Contracts.References;

// POST /api/references/{id}/merge. IntoId is the source that stays; {id} is the duplicate, which is archived.
public sealed record MergeSourceRequest(Guid IntoId);
