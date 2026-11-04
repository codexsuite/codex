namespace Codex.Contracts.Manuscript;

// The client makes the addition's id and sends the whole new scene body (holding its ::kb-addition line) with the
// version it was edited from, so the server never has to interpret an editor position. `Body` is the addition's text.
public sealed record InsertKbAdditionRequest(Guid AdditionId, Guid EntryId, string? Body, string SceneBody, int Version);
