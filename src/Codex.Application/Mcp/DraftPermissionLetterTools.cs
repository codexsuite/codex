using System.ComponentModel;
using Codex.Application.Features.References;
using Codex.Contracts.References;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, asking permission: draft_permission_letter (#20 section F, split out of manage_permission by M10),
// an adapter over the References lane's IPermissionCommands. It writes the letter from the uses and the book's publication
// settings; the author sends it (Codex doesn't send mail) and manage_permission's `mark_sent` records that. Undo withdraws the
// drafted request while it still reads what this call wrote.
[McpServerToolType]
public sealed class DraftPermissionLetterTools(McpSources sources, IServiceProvider services, McpChangeSets changeSets)
{
    [McpServerTool(Name = "draft_permission_letter", Title = "Draft a permission letter", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Drafts the permission request for a source: the letter is written from the uses and the book's print run (override with `letter` and " +
                 "`to_address`). Codex doesn't send mail: the author sends it, then manage_permission `mark_sent` records that. A source with a live request " +
                 "(draft, sent or granted) is a conflict. Undo withdraws the draft while it still reads what this call wrote.")]
    public async Task<PermissionTools.PermissionResult> DraftPermissionLetterAsync(
        McpServer server,
        [Description("The source: an id, a marker like \"#4\", a cite key or a title.")] string source,
        [Description("The letter (Markdown), instead of the one drafted from the uses.")] string? letter = null,
        [Description("Where the request goes, instead of the source's permissions contact.")] string? to_address = null,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var found = await sources.WritableAsync(source, book, cancellationToken);
        var sourceId = found.Row.Id;
        if (letter is { } text && text.Trim().Length == 0)
        {
            throw McpToolException.Invalid("letter", "The letter cannot be empty.");
        }
        var permissions = PermissionTools.CommandsOf(services);
        var caller = McpCaller.From(server);
        var after = await permissions.DraftAsync(sourceId, cancellationToken, caller.Provenance);
        if (letter != null || to_address != null)
        {
            after = await permissions.PatchAsync(after.Permission!.Id, new PatchPermissionRequest(letter, to_address, null), cancellationToken);
        }

        var drafted = after.Permission!;
        var step = new McpUndoStep("withdraw_permission", drafted.Id, Parent: sourceId,
            Now: new McpUndoState(Status: drafted.Status, Body: drafted.Letter, To: drafted.To));
        var undoId = await changeSets.RecordAsync(found.Book.Id, "draft_permission_letter", $"Permission draft for {found.Item.Label}", caller, [step], cancellationToken);
        var reason = McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit);
        return new PermissionTools.PermissionResult("draft", drafted.Status, drafted.To, drafted.Letter, drafted.RemindLabel, drafted.SentLabel,
            after.Verdict.Status, reason)
        {
            Item = found.Item, UndoId = undoId,
            SideEffects = [$"Permission for {found.Item.Label}: drafted, not sent: the author sends the letter, then mark_sent.", $"Clearance: {after.Verdict.Status} ({reason})."],
            Affected = [new McpAffected("permission_request", drafted.Id, "status", null, drafted.Status, found.Item.Label)],
        };
    }
}
