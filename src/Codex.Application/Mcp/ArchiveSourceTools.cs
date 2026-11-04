using System.ComponentModel;
using Codex.Application.Features.References;
using Codex.Contracts.Manuscript;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, removing a source: archive_source (#20 review 2, "archive/restore for sources"), over
// ISourceCommands.ArchiveAsync, the References panel's Remove. Every use's marker is unwrapped into plain text (one revision
// per changed scene), then the uses and the source are archived; a source is never hard-deleted over MCP (Part 6). Undo
// brings the source back (ISourceRestoreCommands), restores each scene's prose, which brings the uses back with their
// links, and points the open quote flags that named it back at it. A scene edited since makes undo conflict.
[McpServerToolType]
public sealed class ArchiveSourceTools(
    McpSources sources, McpRefs refs, ISourceCommands sourceCommands, ISourceRestoreCommands restores, ISourceDetailQueries details,
    IProseStore prose, McpChangeSets changeSets)
{
    public sealed record ArchiveSourceResult(string Archived, int Uses, IReadOnlyList<McpItem> Scenes, bool DryRun) : McpWriteResult;

    [McpServerTool(Name = "archive_source", Title = "Remove a source", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Removes a source from a book's References, as Remove in the References panel: each passage citing it becomes plain text " +
                 "(the words stay, the marker goes, later markers renumber), and the source and its uses are archived. Returns the scenes whose " +
                 "prose changed. dry_run reports the uses and scenes without writing. Undo brings the source, its uses and the prose back while " +
                 "those scenes are unedited since; otherwise conflict. To fold a duplicate into another source use merge_sources instead.")]
    public async Task<ArchiveSourceResult> ArchiveSourceAsync(
        McpServer server,
        [Description("The source: an id, a marker like \"#4\", a cite key or a title.")] string source,
        [Description("Report the uses and scenes without writing.")] bool dry_run = false,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var target = await sources.WritableAsync(source, book, cancellationToken);
        var sourceId = target.Row.Id;
        var label = target.Item.Label;
        var uses = (await details.GetAsync(sourceId, cancellationToken)).Uses.Where(u => !u.Pending).ToList();
        var outline = await refs.OutlineAsync(target.Book, McpRefs.SessionKey(server), cancellationToken);
        McpItem SceneItem(Guid id) => outline.SceneById(id)?.Item(target.Book.Id) ?? new McpItem(id, "scene", "an archived scene", CodexLinks.Write(target.Book.Id));

        if (dry_run)
        {
            var planned = uses.Select(u => u.SceneId).Distinct().Select(SceneItem).ToList();
            return new ArchiveSourceResult(label, uses.Count, planned, true)
            {
                Item = target.Item,
                SideEffects = [$"Would remove {label}: {Passages(uses.Count)} would become plain text{In(planned)}. Nothing was written."],
            };
        }

        var caller = McpCaller.From(server);
        var flags = await restores.FlagsAsync(sourceId, cancellationToken);
        var before = new Dictionary<Guid, string>();
        foreach (var sceneId in uses.Select(u => u.SceneId).Distinct())
        {
            before[sceneId] = (await prose.GetAsync(sceneId, cancellationToken)).Body;
        }

        var result = await sourceCommands.ArchiveAsync(sourceId, cancellationToken, caller.Provenance);

        List<McpUndoStep> steps = [new McpUndoStep("restore_source", sourceId, Labels: flags)];
        var affected = new List<McpAffected> { new("source", sourceId, "archived", "false", "true", label) };
        foreach (var sceneId in result.Scenes.Select(s => s.SceneId))
        {
            var body = before[sceneId];
            await changeSets.CutAgentRevisionAsync(sceneId, body, caller, cancellationToken);
            var version = (await prose.GetAsync(sceneId, cancellationToken)).Version;
            steps.Add(new McpUndoStep("prose", sceneId, Body: body, Version: version));
            affected.Add(new McpAffected("scene", sceneId, "version", null, version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        var undoId = await changeSets.RecordAsync(target.Book.Id, "archive_source", $"Removed {label}", caller, steps, cancellationToken);
        await refs.OutlineAsync(target.Book, McpRefs.SessionKey(server), cancellationToken);

        var scenes = result.Scenes.Select(s => SceneItem(s.SceneId)).ToList();
        var effects = new List<string> { $"Removed {label}: {Passages(result.Scenes.Sum(s => s.Uses))} became plain text{In(scenes)}." };
        if (target.Row.Number != null)
        {
            effects.Add("Later markers moved up by one.");
        }
        if (flags.Count > 0)
        {
            effects.Add($"{flags.Count} open unflagged quote{(flags.Count == 1 ? "" : "s")} no longer suggest{(flags.Count == 1 ? "s" : "")} it.");
        }
        return new ArchiveSourceResult(label, result.Scenes.Sum(s => s.Uses), scenes, false)
        {
            Item = target.Item, UndoId = undoId, SideEffects = effects, Affected = affected,
            Version = steps.Count == 2 ? steps[1].Version : null,
        };
    }

    private static string Passages(int n) => $"{n} cited passage{(n == 1 ? "" : "s")}";

    private static string In(IReadOnlyList<McpItem> scenes) => scenes.Count == 0 ? "" : $" in {string.Join(", ", scenes.Select(s => s.Label))}";
}
