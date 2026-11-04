using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.KnowledgeBase;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// E. Knowledge base, ending an entry's life: delete_kb_entry and merge_kb_entries (#78), adapters over IKbDeleteCommands and
// IKbMergeCommands (the Plan screen's Delete and Merge). Both are one transaction, both can be previewed with dry_run, and both
// are undoable: the delete's own undo record and the merge's KbMergeSnapshot travel in the undo step's Body, and undo puts it
// all back while nothing the write touched has changed since (conflict otherwise, nothing undone). The scene bodies they edit
// get the agent's revision, as every other MCP prose write does.
[McpServerToolType]
public sealed class KbEntryLifecycleTools(McpRefs refs, IKbDeleteCommands deletes, IKbMergeCommands merges, McpChangeSets changeSets)
{
    public sealed record DeleteKbEntryResult(bool DryRun, string Entry, int Mentions, int Scenes, int Notes, int Links) : McpWriteResult;

    public sealed record MergeKbEntriesResult(
        bool DryRun, string Merged, string Into, int SceneLinks, int Additions, int Related, IReadOnlyList<string> NewAliases) : McpWriteResult;

    [McpServerTool(Name = "delete_kb_entry", Title = "Delete a Knowledge base entry", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Deletes a Knowledge base entry (a soft delete). Every mention of it in scene prose, summaries, beats and other entries becomes plain text, " +
                 "its timeline notes are removed, and its scene, beat and related links are dropped. dry_run reports what would change (mentions, scenes, " +
                 "notes, links) without writing. Undo brings the entry, its mentions, notes and links back while nothing it touched has changed since; otherwise conflict.")]
    public async Task<DeleteKbEntryResult> DeleteKbEntryAsync(
        McpServer server,
        [Description("The entry: id, name or alias.")] string entry,
        [Description("Report what would change without writing.")] bool dry_run = false,
        [Description("The book, to scope a name or alias.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var found = await refs.EntryAsync(entry, book == null ? null : await refs.BookAsync(book, cancellationToken), cancellationToken);
        var impact = await deletes.ImpactAsync(found.Id, cancellationToken);
        if (dry_run)
        {
            return new DeleteKbEntryResult(true, found.Name, impact.Mentions, impact.Scenes, impact.Notes, impact.Links)
            {
                Item = found.Item,
                SideEffects = [$"Would turn {impact.Mentions} mention{Plural(impact.Mentions)} in {impact.Scenes} scene{Plural(impact.Scenes)} into plain text, remove {impact.Notes} timeline note{Plural(impact.Notes)} and {impact.Links} related link{Plural(impact.Links)}, and delete {found.Name}."],
                Affected = [new McpAffected("kb_entry", found.Id, "deleted", "false", "true", found.Name)],
            };
        }

        var record = await deletes.DeleteAsync(found.Id, cancellationToken);
        foreach (var scene in record.Scenes)
        {
            await changeSets.CutAgentRevisionAsync(scene.SceneId, scene.Body, caller, cancellationToken);
        }
        var step = new McpUndoStep("restore_entry", found.Id, Body: JsonSerializer.Serialize(record, McpJson.Options));
        var undoId = await changeSets.RecordAsync(found.LinkBookId ?? found.OwnerId, "delete_kb_entry", $"Deleted Knowledge base entry {found.Name}", caller, [step], cancellationToken);
        return new DeleteKbEntryResult(false, found.Name, impact.Mentions, impact.Scenes, impact.Notes, impact.Links)
        {
            Item = found.Item, UndoId = undoId,
            SideEffects = [$"Deleted {found.Name}; {impact.Mentions} mention{Plural(impact.Mentions)} in {impact.Scenes} scene{Plural(impact.Scenes)} are plain text now, {impact.Notes} timeline note{Plural(impact.Notes)} and {impact.Links} related link{Plural(impact.Links)} removed."],
            Affected = [new McpAffected("kb_entry", found.Id, "deleted", "false", "true", found.Name)],
        };
    }

    [McpServerTool(Name = "merge_kb_entries", Title = "Merge a duplicate Knowledge base entry", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Folds the duplicate `entry` into the entry to keep, `into`, which must be in the same book or the duplicate's series. Every use of the duplicate " +
                 "(mentions, printed spells and stat blocks, summaries, beats, other entries' text) points at the kept entry; scene links, beat context, timeline notes, " +
                 "related links and sources move over; the kept entry gains the duplicate's name and aliases as aliases and its body under \"Merged from <name>\" " +
                 "(its own summary and body stay). The duplicate is archived. dry_run reports what would move without writing. " +
                 "Undo restores the duplicate and puts everything back while nothing the merge touched has changed since; otherwise conflict.")]
    public async Task<MergeKbEntriesResult> MergeKbEntriesAsync(
        McpServer server,
        [Description("The duplicate: id, name or alias. It is archived.")] string entry,
        [Description("The entry to keep: id, name or alias.")] string into,
        [Description("Report what would move without writing.")] bool dry_run = false,
        [Description("The book, to scope names and aliases.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var caller = McpCaller.From(server);
        var scopeBook = book == null ? null : await refs.BookAsync(book, cancellationToken);
        var duplicate = await refs.EntryAsync(entry, scopeBook, cancellationToken);
        // An id names the entry wherever it is, so a target in another book fails invalid; a name is looked up in the duplicate's own scope.
        var keptScope = scopeBook ?? (Guid.TryParse(into.Trim(), out _) || duplicate.LinkBookId is not { } linked ? null : await refs.BookAsync(linked.ToString(), cancellationToken));
        var kept = await refs.EntryAsync(into, keptScope, cancellationToken);
        if (duplicate.Id == kept.Id)
        {
            throw McpToolException.Invalid("into", "An entry can't be merged into itself: `into` names the same entry as `entry`.");
        }

        if (dry_run)
        {
            var impact = await merges.ImpactAsync(duplicate.Id, kept.Id, cancellationToken);
            return new MergeKbEntriesResult(true, duplicate.Name, kept.Name, impact.SceneLinks, impact.Additions, impact.Related, impact.NewAliases)
            {
                Item = kept.Item,
                SideEffects = Effects("Would move", "would become", duplicate.Name, kept.Name, impact.SceneLinks, impact.Additions, impact.Related, impact.NewAliases),
                Affected = Affected(duplicate, kept, impact.NewAliases),
            };
        }

        var impactBefore = await merges.ImpactAsync(duplicate.Id, kept.Id, cancellationToken);
        var result = await merges.MergeWithSnapshotAsync(duplicate.Id, kept.Id, cancellationToken);
        foreach (var scene in result.Snapshot.Scenes)
        {
            await changeSets.CutAgentRevisionAsync(scene.SceneId, scene.Body, caller, cancellationToken);
        }
        var step = new McpUndoStep("unmerge_entry", duplicate.Id, Parent: kept.Id, Body: JsonSerializer.Serialize(result.Snapshot, McpJson.Options));
        var undoId = await changeSets.RecordAsync(kept.LinkBookId ?? kept.OwnerId, "merge_kb_entries", $"Merged Knowledge base entry {duplicate.Name} into {kept.Name}", caller, [step], cancellationToken);
        return new MergeKbEntriesResult(false, duplicate.Name, kept.Name, impactBefore.SceneLinks, impactBefore.Additions, impactBefore.Related, impactBefore.NewAliases)
        {
            Item = kept.Item, UndoId = undoId,
            SideEffects = Effects("Moved", "became", duplicate.Name, kept.Name, impactBefore.SceneLinks, impactBefore.Additions, impactBefore.Related, impactBefore.NewAliases),
            Affected = Affected(duplicate, kept, impactBefore.NewAliases),
        };
    }

    private static List<string> Effects(string moved, string became, string from, string to, int sceneLinks, int additions, int related, IReadOnlyList<string> aliases)
    {
        var effects = new List<string>
        {
            $"{moved} {sceneLinks} scene link{Plural(sceneLinks)}, {additions} timeline note{Plural(additions)} and {related} related link{Plural(related)} from {from} to {to}.",
            $"Every use of {from} in the text points at {to}, and {from} {(moved == "Moved" ? "is" : "would be")} archived.",
        };
        if (aliases.Count > 0)
        {
            effects.Add($"{string.Join(", ", aliases)} {became} alias{(aliases.Count == 1 ? "" : "es")} of {to}.");
        }
        return effects;
    }

    private static List<McpAffected> Affected(McpEntry duplicate, McpEntry kept, IReadOnlyList<string> aliases) =>
    [
        new("kb_entry", duplicate.Id, "archived", "false", "true", duplicate.Name),
        new("kb_entry", kept.Id, "aliases", null, aliases.Count == 0 ? "+0" : $"+{string.Join(", ", aliases)}", kept.Name),
    ];

    private static string Plural(int count) => count == 1 ? "" : "s";
}
