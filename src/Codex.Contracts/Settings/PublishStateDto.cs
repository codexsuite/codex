namespace Codex.Contracts.Settings;

// GET /api/books/{id}/publish (and the POST answer): what the Published dialog shows and the snapshot list.
// WordCount is the live manuscript's words, ChapterCount its live chapters (0 means nothing to publish) and
// AtRiskCount the sources in References that need permission (they block Export for printer, not publishing).
// ExportRunning is true while a source export of the book is queued or running; a snapshot taken then waits for it.
public sealed record PublishStateDto(
    string Status,
    int WordCount,
    int ChapterCount,
    int AtRiskCount,
    bool ExportRunning,
    IReadOnlyList<SnapshotDto> Snapshots);
