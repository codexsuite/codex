namespace Codex.Contracts.Manuscript;

// One scene's stored Markdown. A scene with no prose row reads as an empty body at version 0, as in ProseDto.
public sealed record ManuscriptSceneDto(Guid SceneId, string Body, int Version);
