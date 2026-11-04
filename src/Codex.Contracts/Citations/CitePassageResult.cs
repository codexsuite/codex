namespace Codex.Contracts.Citations;

// The new reference_use and what citing did: the scene's new prose version and body, the source's marker number, how
// many other sources' numbers moved up because of it, and the source's clearance status and reason (the
// reference_clearance codes) before and after.
public sealed record CitePassageResult(
    Guid UseId,
    Guid SceneId,
    int Version,
    string Body,
    int Number,
    int MarkersShifted,
    string StatusBefore,
    string Status,
    string Reason);
