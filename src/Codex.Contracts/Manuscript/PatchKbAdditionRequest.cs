namespace Codex.Contracts.Manuscript;

// Null leaves a field as it is. A new entry must be in the scene's book scope.
public sealed record PatchKbAdditionRequest(string? Body, Guid? EntryId);
