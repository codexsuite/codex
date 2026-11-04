using System.Text.Json;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// The History list: reference_event.action codes written by the source commands, and their wording.
public static class SourceEvents
{
    public const string Added = "added";
    public const string SignedOff = "signed_off";
    public const string Reopened = "reopened";
    public const string FairUseAccepted = "fair_use_accepted";
    public const string DetailsEdited = "details_edited";
    public const string EntryLinked = "entry_linked";
    public const string EntryUnlinked = "entry_unlinked";
    public const string UseRetyped = "use_retyped";
    public const string UseLocatorChanged = "use_locator_changed";
    public const string LinksChecked = "links_checked";
    public const string RedirectAccepted = "redirect_accepted";
    public const string Archived = "archived";
    public const string Merged = "merged";
    public const string PermissionFollowedUp = "permission_followed_up";
    public const string PermissionLetterRemoved = "permission_letter_removed";
    public const string PermissionLetterAttached = "permission_letter_attached";
    public const string PermissionDrafted = "permission_drafted";
    public const string PermissionSent = "permission_sent";
    public const string PermissionGranted = "permission_granted";
    public const string PermissionRefused = "permission_refused";
    public const string PermissionWithdrawn = "permission_withdrawn";
    public const string MergedIn = "merged_in";
    public const string MergedInto = "merged_into";
    public const string MergeUndone = "merge_undone";
    public const string Restored = "restored";

    // provenance: who the line is written as (the author when null); see EventProvenance.
    public static async Task RecordAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sourceId, Guid actorId, string action, object detail,
        CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        var by = provenance ?? new EventProvenance();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO reference_event (reference_id, actor_id, source, action, detail, client_name, api_token_id, api_token_name)
            VALUES (@sourceId, @actorId, @source, @action, @detail::jsonb, @clientName, @tokenId, @tokenName)
            """,
            new
            {
                sourceId, actorId, source = by.Source, action, detail = JsonSerializer.Serialize(detail), clientName = by.ClientName,
                tokenId = by.TokenId, tokenName = by.TokenName,
            },
            transaction, cancellationToken: cancellationToken));
    }

    public static string Text(string action, string detailJson)
    {
        using var detail = JsonDocument.Parse(detailJson);
        string Get(string key) => detail.RootElement.TryGetProperty(key, out var v) ? v.ToString() : string.Empty;
        return action switch
        {
            Added => Get("how").Length == 0 ? "added the source" : $"added the source from {Get("how")}",
            SignedOff => "marked the source as cleared",
            Reopened => Get("why").Length == 0 ? "reopened the source" : $"reopened the source: {Get("why")}",
            FairUseAccepted => $"accepted fair use: {Get("reason")}",
            DetailsEdited => $"edited {Get("fields")}",
            EntryLinked => $"linked the Codex entry {Get("name")}",
            EntryUnlinked => "removed the Codex link",
            UseRetyped => $"changed a passage from {Get("from")} to {Get("to")}",
            UseLocatorChanged => $"changed a passage's locator from {Get("from")} to {Get("to")}",
            LinksChecked => $"re-checked {Get("fields")}",
            RedirectAccepted => $"accepted the new {Get("field")} address",
            Archived => "removed the source",
            Merged => $"merged {Get("title")} into this source ({Get("uses")} uses moved)",
            PermissionFollowedUp => "followed up on the permission request",
            PermissionLetterRemoved => "removed the permission letter",
            PermissionLetterAttached => "attached a permission letter",
            PermissionDrafted => "drafted a permission request",
            PermissionSent => "marked the permission request as sent",
            PermissionGranted => "recorded that permission was granted",
            PermissionRefused => Get("note").Length == 0 ? "recorded that permission was refused" : $"recorded that permission was refused: {Get("note")}",
            PermissionWithdrawn => "withdrew the permission request",
            MergedIn => $"merged {Get("from")} into this source ({Get("uses")} uses{(Get("fields").Length == 0 ? string.Empty : $", filled {Get("fields")}")})",
            MergedInto => $"merged this source into {Get("into")}",
            MergeUndone => $"undid the merge with {Get("other")}",
            Restored => "brought the source back",
            _ => action.Replace('_', ' '),
        };
    }
}
