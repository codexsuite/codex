namespace Codex.Application.Features.Import;

public sealed record ImportChapterPlan(
    string Title,
    string? Summary,
    string? Notes,
    bool IsNumbered,
    IReadOnlyList<ImportScenePlan> Scenes);
