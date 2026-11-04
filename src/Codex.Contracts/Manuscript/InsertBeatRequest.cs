namespace Codex.Contracts.Manuscript;

// The client makes the new beat's id and sends the whole new scene body (holding its ::beat line) with the version it was
// edited from, so the server never has to interpret an editor position.
public sealed record InsertBeatRequest(Guid BeatId, string Body, int Version);
