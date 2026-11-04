namespace Codex.Contracts.Structure;

// PUT /api/scenes/{id}/labels: the scene's whole label set. Labels must be in the book's scope (its own or its series').
public sealed record SetSceneLabelsRequest(IReadOnlyList<Guid> LabelIds);
