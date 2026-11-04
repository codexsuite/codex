namespace Codex.Contracts.References;

// One possible unflagged quote awaiting review. Reason is quotation_marks or source_match; Why is the server's
// wording of it. Excerpt is the quoted text. ReferenceId/ReferenceLabel are the best guess ("Whitlock, 2021") when
// the scan matched a source, MatchPercent its score.
public sealed record QuoteFlagDto(
    Guid Id,
    Guid SceneId,
    int? ChapterNumber,
    string ChapterTitle,
    int SceneNumber,
    string Reason,
    string Why,
    string Excerpt,
    Guid? ReferenceId,
    string? ReferenceLabel,
    int? MatchPercent,
    string? Locator);
