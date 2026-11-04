using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.References;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, folding a duplicate into a kept source: merge_sources (#20 section F, split out of update_source by
// M10), an adapter over ISourceMergeCommands (SourceMergeCommands says exactly what moves). The duplicate's uses move to the kept
// source in one transaction; a `ref:` link in prose names the use, so no prose changes. Undo puts it all back while nothing the
// merge touched has changed since; after that it is conflict, and nothing is undone.
[McpServerToolType]
public sealed class MergeSourcesTools(McpSources sources, ISourceMergeCommands merges, ISourceDetailQueries details, McpChangeSets changeSets)
{
    public sealed record MergeSourcesResult(
        bool DryRun, string Merged, string StatusBefore, string Status, string Reason, int QuotedWords, int Limit,
        int UsesMoved, IReadOnlyList<string> FilledFields, bool PermissionMoved) : McpWriteResult;

    [McpServerTool(Name = "merge_sources", Title = "Merge a duplicate source", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Folds the duplicate `source` into the source to keep, `into`, in the same book. The duplicate's uses move to the kept source (markers " +
                 "renumber; the prose is not edited, since a use's link names the use, and a moved use counts as new, so a signed-off kept source reopens). " +
                 "The kept source's empty fields are filled from the duplicate; fields with a value stay. A live permission request moves too when the kept source " +
                 "has none. The duplicate is archived. dry_run reports what would move and the new clearance without writing. Returns the kept source's " +
                 "clearance before and after. Undo restores the duplicate and moves its uses back while nothing the merge touched has changed since; otherwise conflict.")]
    public async Task<MergeSourcesResult> MergeSourcesAsync(
        McpServer server,
        [Description("The duplicate: an id, a marker like \"#4\", a cite key or a title. It is archived.")] string source,
        [Description("The source to keep, in the same book: an id, a marker, a cite key or a title.")] string into,
        [Description("Report what would move and the new clearance without writing.")] bool dry_run = false,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var duplicate = await sources.WritableAsync(source, book, cancellationToken);
        var kept = await sources.SourceAsync(into, duplicate.Book.Id.ToString(), cancellationToken);
        if (duplicate.Row.Id == kept.Row.Id)
        {
            throw McpToolException.Invalid("into", "A source can't be merged into itself: `into` names the same source as `source`.");
        }
        if (duplicate.Book.Id != kept.Book.Id)
        {
            throw McpToolException.Invalid("into", "Both sources must be in the same book to merge them.");
        }

        var keptId = kept.Row.Id;
        var before = await details.GetAsync(keptId, cancellationToken);
        var caller = McpCaller.From(server);
        var result = await merges.MergeAsync(duplicate.Row.Id, keptId, dry_run, cancellationToken, caller.Provenance);
        var after = result.Kept;
        var reason = McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit);

        string? undoId = null;
        if (!dry_run)
        {
            var step = new McpUndoStep("unmerge_source", duplicate.Row.Id, Parent: keptId, Body: JsonSerializer.Serialize(result.Snapshot, McpJson.Options));
            undoId = await changeSets.RecordAsync(duplicate.Book.Id, "merge_sources", $"Merged {duplicate.Item.Label} into {kept.Item.Label}", caller, [step], cancellationToken);
        }

        var verb = dry_run ? "Would move" : "Moved";
        var effects = new List<string>
        {
            $"{verb} {result.UsesMoved} use{(result.UsesMoved == 1 ? "" : "s")} from {duplicate.Item.Label} to {kept.Item.Label}{(result.ArchivedUsesMoved > 0 ? $" ({result.ArchivedUsesMoved} already removed from the prose came along)" : "")}.",
            $"{(dry_run ? "Would archive" : "Archived")} {duplicate.Item.Label}.",
            $"Clearance of {kept.Item.Label}: {before.Verdict.Status} → {after.Verdict.Status} ({reason}).",
        };
        if (result.FilledFields.Count > 0)
        {
            effects.Add($"{(dry_run ? "Would fill" : "Filled")} {string.Join(", ", result.FilledFields)} on {kept.Item.Label} from the duplicate.");
        }
        if (result.PermissionMoved)
        {
            effects.Add($"The duplicate's permission request {(dry_run ? "would move" : "moved")} to {kept.Item.Label}.");
        }
        var affected = new List<McpAffected>
        {
            new("source", duplicate.Row.Id, "archived", "false", "true", duplicate.Item.Label),
            new("source", keptId, "uses", null, $"+{result.UsesMoved}", kept.Item.Label),
        };
        if (before.Verdict.Status != after.Verdict.Status)
        {
            affected.Add(new McpAffected("source", keptId, "status", before.Verdict.Status, after.Verdict.Status, kept.Item.Label));
        }

        return new MergeSourcesResult(dry_run, duplicate.Item.Label, before.Verdict.Status, after.Verdict.Status, reason, after.QuotedWords, after.Limit,
            result.UsesMoved, result.FilledFields, result.PermissionMoved)
        {
            Item = new McpItem(keptId, "source", McpSources.Label(after.Number, after.Title), CodexLinks.References(kept.Book.Id, keptId), after.Number is { } n ? $"#{n}" : null),
            UndoId = undoId, SideEffects = effects, Affected = affected,
        };
    }
}
