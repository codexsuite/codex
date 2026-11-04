namespace Codex.Contracts.References;

// One source marker in the prose, in reading order. Number is its source's number (order of first use in the
// book, the one Write shows), so several markers can share one. Status is the stored clearance status of the
// source: cleared, needs_check or risk. Reason is the clearance code the UI words. QuotedWords is the source's
// quoted total across the book and Limit the book's quote limit. GenerationId is set while the marker exists
// only in a pending AI draft.
public sealed record MarkerDto(
    Guid UseId,
    int Number,
    string Status,
    Guid SourceId,
    string SourceTitle,
    string Reason,
    string Kind,
    int QuotedWords,
    int Limit,
    Guid SceneId,
    int? ChapterNumber,
    int SceneNumber,
    Guid? GenerationId);
