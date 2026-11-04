namespace Codex.Contracts.Print;

// What the Export manuscript dialog needs: the live chapters to pick from and how many sources are at risk
// (the export is not gated on them, the dialog only says so).
public sealed record ManuscriptExportInfoDto(IReadOnlyList<ManuscriptChapterDto> Chapters, int AtRiskSources);
