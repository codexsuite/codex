namespace Codex.Contracts.References;

// POST /api/references/{id}/recheck. Fields lists the identifiers that were looked up; Source is the refreshed panel.
public sealed record RecheckResultDto(IReadOnlyList<RecheckFieldDto> Fields, SourceDetailDto Source);
