namespace Codex.Contracts.Structure;

// POST /api/acts/{id}/archive ("Delete act"): the uses that dropped out of the reference outline with the act's scenes.
public sealed record ArchiveActResult(Guid ActId, IReadOnlyList<SceneUseDto> ArchivedUses);
