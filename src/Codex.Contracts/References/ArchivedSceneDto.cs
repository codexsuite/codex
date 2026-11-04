namespace Codex.Contracts.References;

public sealed record ArchivedSceneDto(Guid SceneId, int? ChapterNumber, int SceneNumber, int Uses);
