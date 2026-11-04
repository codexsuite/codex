namespace Codex.Contracts.Structure;

// One source use that an archive took out of (or a restore brought back into) the book's clearance.
public sealed record SceneUseDto(Guid UseId, Guid SourceId, string SourceTitle);
