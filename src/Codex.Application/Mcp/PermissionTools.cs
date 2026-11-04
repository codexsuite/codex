using System.ComponentModel;
using Codex.Application.Features.References;
using Codex.Contracts.References;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, permission requests: manage_permission (#20 section F), an adapter over the References
// lane's IPermissionCommands. Codex doesn't send mail: draft_permission_letter writes the letter from the uses and the book's
// publication settings (DraftPermissionLetterTools), the author sends it, and `mark_sent` records that. `record_grant` is the
// author's call, and the signed letter is a file only the author can attach, so over MCP it returns blocked with where to do
// it. None of these steps can be undone here; undoing a drafted letter is draft_permission_letter's.
[McpServerToolType]
public sealed class PermissionTools(McpSources sources, IServiceProvider services, ISourceDetailQueries details, McpChangeSets changeSets)
{
    private static readonly string[] Actions = ["update", "mark_sent", "set_reminder", "record_grant", "record_refusal", "withdraw"];

    // The permission commands keep signed letters in the blob store, which needs CODEX_BLOB_ROOT; a server without it still serves every other
    // tool, so the commands are resolved here, when a permission tool needs them, and a missing folder is not_configured.
    public static IPermissionCommands CommandsOf(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<IPermissionCommands>();
        }
        catch (InvalidOperationException e) when (e.Message.Contains("CODEX_BLOB_ROOT", StringComparison.Ordinal))
        {
            throw new McpToolException(McpErrorCodes.NotConfigured,
                "Permission requests keep signed letters in the blob store, and this server has no CODEX_BLOB_ROOT. Set it to the folder the web app uses.");
        }
    }

    public sealed record PermissionResult(
        string Action, string Status, string To, string Letter, string? Reminder, string? SentLabel, string SourceStatus, string SourceReason) : McpWriteResult;

    [McpServerTool(Name = "manage_permission", Title = "Ask the rights holder", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Moves a source's permission request along; draft it first with draft_permission_letter. `update` edits a draft's letter and to_address. " +
                 "`mark_sent` records that it was sent (the author sends it, since Codex doesn't send mail; a reminder is set two weeks out). `set_reminder` " +
                 "takes remind_at (yyyy-MM-dd, empty clears). `record_refusal` takes an optional `note`. `withdraw` drops a draft or sent request. " +
                 "`record_grant` is the author's decision and needs the signed letter as a file, so it returns blocked with the References link. " +
                 "None of these steps can be undone here.")]
    public async Task<PermissionResult> ManagePermissionAsync(
        McpServer server,
        [Description("The source: an id, a marker like \"#4\", a cite key or a title.")] string source,
        [Description("update, mark_sent, set_reminder, record_grant, record_refusal or withdraw.")] string action,
        [Description("The letter (Markdown), for update.")] string? letter = null,
        [Description("Where the request goes, for update.")] string? to_address = null,
        [Description("The reminder date, yyyy-MM-dd.")] string? remind_at = null,
        [Description("Why permission was refused, for record_refusal.")] string? note = null,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (action == "draft")
        {
            throw McpToolException.Invalid("action", "Drafting a request is its own tool now: use draft_permission_letter.");
        }
        if (!Actions.Contains(action, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("action", $"\"{action}\" is not an action: use {string.Join(", ", Actions)}.");
        }
        var found = await sources.WritableAsync(source, book, cancellationToken);
        var sourceId = found.Row.Id;
        var before = await details.GetAsync(sourceId, cancellationToken);
        var request = before.Permission is { Status: "draft" or "sent" or "granted" } live ? live : null;
        var url = CodexLinks.References(found.Book.Id, sourceId);
        if (action == "record_grant")
        {
            throw new McpToolException(McpErrorCodes.Blocked,
                $"Recording a grant is the author's decision and needs the signed letter as a file, which can't be attached over MCP. Ask them to record it in References ({url}).")
            { Rule = "signed_letter_is_a_file" };
        }
        if (request == null)
        {
            throw new McpToolException(McpErrorCodes.Conflict, $"{found.Item.Label} has no permission request to {action.Replace('_', ' ')}: draft one first.");
        }

        // What the commands would refuse in the second command of a call, checked before the first writes, so a refused call writes nothing.
        if (action == "update" && letter is { } text && text.Trim().Length == 0)
        {
            throw McpToolException.Invalid("letter", "The letter cannot be empty.");
        }
        if (action is "update" or "set_reminder" && remind_at is { Length: > 0 } day
            && !DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        {
            throw McpToolException.Invalid("remind_at", "The reminder date must be written yyyy-MM-dd.");
        }
        var permissions = CommandsOf(services);
        var caller = McpCaller.From(server);
        SourceDetailDto after;
        switch (action)
        {
            case "update":
                after = await permissions.PatchAsync(request.Id, new PatchPermissionRequest(letter, to_address, remind_at), cancellationToken);
                break;
            case "set_reminder":
                after = await permissions.PatchAsync(request.Id, new PatchPermissionRequest(null, null, remind_at ?? ""), cancellationToken);
                break;
            case "mark_sent":
                after = await permissions.MarkSentAsync(request.Id, cancellationToken, caller.Provenance);
                break;
            case "record_refusal":
                after = await permissions.RefuseAsync(request.Id, new RefusePermissionRequest(note), cancellationToken, caller.Provenance);
                break;
            default:
                after = await permissions.WithdrawAsync(request.Id, cancellationToken, caller.Provenance);
                break;
        }

        var step = new McpUndoStep("unsupported", sourceId, Title: $"a permission request that was {Past(action)} can't be taken back over MCP");
        var undoId = await changeSets.RecordAsync(found.Book.Id, "manage_permission", $"Permission {action.Replace('_', ' ')} for {found.Item.Label}", caller, [step], cancellationToken);
        var current = after.Permission;
        var reason = McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit);
        return new PermissionResult(action, current?.Status ?? "withdrawn", current?.To ?? "", current?.Letter ?? "", current?.RemindLabel, current?.SentLabel,
            after.Verdict.Status, reason)
        {
            Item = found.Item, UndoId = undoId,
            SideEffects = [$"Permission for {found.Item.Label}: {Past(action)}. {(current?.SentLabel ?? current?.RemindLabel) ?? ""}".Trim(), $"Clearance: {after.Verdict.Status} ({reason})."],
            Affected = [new McpAffected("permission_request", current?.Id ?? request.Id, "status", request.Status, current?.Status ?? "withdrawn", found.Item.Label)],
        };
    }

    public static string Past(string action) => action switch
    {
        "update" => "updated",
        "mark_sent" => "marked as sent",
        "set_reminder" => "reminder set",
        "record_refusal" => "recorded as refused",
        _ => "withdrawn",
    };
}
