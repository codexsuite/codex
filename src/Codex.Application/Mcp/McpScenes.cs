using Agoda.IoC.Core;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Resolves a scene ref for a write tool: an id names its own book, a display number ("4.1") is read in `book` (the
// most recently edited live book when omitted). The outline is recorded for stale_ref like any read.
[RegisterSingleton]
public sealed class McpScenes(McpRefs refs, McpQueries queries)
{
    public sealed record Target(BookOutline Outline, BookOutline.Scene Scene)
    {
        public McpItem Item => Scene.Item(Outline.Book.Id);
    }

    public async Task<Target> SceneAsync(McpServer server, string? book, string scene, CancellationToken cancellationToken)
    {
        var bookRef = book;
        if (Guid.TryParse(scene.Trim(), out var sceneId))
        {
            bookRef = (await queries.BookOfAsync(sceneId, null, cancellationToken)
                       ?? throw McpToolException.NotFound($"No scene {sceneId}.")).ToString();
        }
        var outline = await refs.OutlineAsync(await refs.BookAsync(bookRef, cancellationToken), McpRefs.SessionKey(server), cancellationToken);
        var item = McpRefs.Item(outline, scene);
        return item.Kind == "scene"
            ? new Target(outline, outline.SceneById(item.Id)!)
            : throw McpToolException.Invalid("scene", $"{item.Label} is a {item.Kind}, not a scene.");
    }

    // The same scene in a fresh outline, after a write (its words and version have moved on).
    public async Task<Target> ReloadAsync(McpServer server, Target target, CancellationToken cancellationToken)
    {
        var outline = await refs.OutlineAsync(target.Outline.Book, McpRefs.SessionKey(server), cancellationToken);
        return new Target(outline, outline.SceneById(target.Scene.Dto.Id) ?? target.Scene);
    }
}
