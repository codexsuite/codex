namespace Codex.Contracts.Events;

// A scene's prose was saved. Version is prose.version after the save; Origin names the writer
// ("editor", "api", "mcp", ...) so a client can ignore its own echo.
public sealed record ProseSaved(Guid BookId, Guid SceneId, int Version, string Origin) : BookEvent(BookId);
