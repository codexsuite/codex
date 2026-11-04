namespace Codex.Contracts.Print;

// The scene's prose version after the line was taken out.
public sealed record RemoveFigureResult(Guid SceneId, int Version);
