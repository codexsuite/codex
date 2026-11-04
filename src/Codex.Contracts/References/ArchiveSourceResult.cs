namespace Codex.Contracts.References;

// POST /api/references/{id}/archive. Scenes lists the scenes whose prose lost its markers (the text stays).
public sealed record ArchiveSourceResult(IReadOnlyList<ArchivedSceneDto> Scenes);
