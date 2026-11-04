namespace Codex.Contracts.Manuscript;

// The two new bodies after the block moved. A beat whose line is now in ToBody and whose row belongs to FromSceneId
// moves to ToSceneId with it.
public sealed record MoveBlockRequest(Guid FromSceneId, int FromVersion, string FromBody, Guid ToSceneId, int ToVersion, string ToBody);
