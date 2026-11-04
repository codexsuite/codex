namespace Codex.Application.Features.Import;

// Status is a stored scene.status value. Entries name ImportEntryPlan.Key values (scene_kb links). Prose is the
// stored-format body that follows the beat lines, or null when the scene has no prose. Figures are the figures its
// prose places (one ::figure line each).
public sealed record ImportScenePlan(
    string? Title,
    string? Summary,
    string? PlanNotes,
    string Status,
    string? ImportRef,
    bool Archived,
    IReadOnlyList<string> Beats,
    string? Prose,
    IReadOnlyList<string> Entries,
    IReadOnlyList<ImportFigurePlan>? Figures = null);
