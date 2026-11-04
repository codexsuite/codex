namespace Codex.Contracts.Citations;

// The words are plain text again and the use is archived (by the prose triggers). MarkersShifted counts the other
// sources whose numbers moved down; Status is the source's clearance status after (not_cited when it was the last use).
public sealed record RemoveCitationResult(
    Guid UseId,
    Guid SceneId,
    int Version,
    string Body,
    int MarkersShifted,
    string Status);
