namespace Codex.Application.Features.Import;

// Opener is the act's part-opener art (act.opener_asset_id), when the source has one.
public sealed record ImportActPlan(string Title, string? Summary, IReadOnlyList<ImportChapterPlan> Chapters, ImportOpenerPlan? Opener = null);
