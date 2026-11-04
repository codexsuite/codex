namespace Codex.Contracts.Manuscript;

// GET /api/scenes/{id}/prose. A scene with no prose row yet reads as an empty body at version 0.
public sealed record ProseDto(Guid SceneId, string Body, int Version, DateTimeOffset UpdatedAt);
