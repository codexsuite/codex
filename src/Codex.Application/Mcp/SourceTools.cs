using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, fixing a source: update_source (#20 section F; the use-level changes moved to update_source_use,
// M1/M10), an adapter over the References lane's ISourceCommands (Patch, Recheck, AcceptRedirect) and ISourceExtrasCommands
// (cite_key, permissions_contact). The arguments are checked before anything is written; a redirect to accept is looked for
// first (a read-only lookup), so a call that can't accept one writes nothing. One change set: the source's fields, Codex
// link, cite key and permissions contact go back on undo.
[McpServerToolType]
public sealed class SourceTools(
    McpRefs refs, McpSources sources, ISourceCommands commands, ISourceExtrasCommands extras, ISourceDetailQueries details, ISourceLookup lookup,
    McpChangeSets changeSets)
{    private static readonly string[] Identifiers = ["isbn", "doi", "url"];

    // The fields update_source takes: the References panel's, and the cite key and permissions contact (ISourceExtrasCommands).
    private static readonly string[] Fields =
    [
        "kind", "title", "authors", "year", "publisher", "edition", "isbn", "doi", "url", "accessed_on", "licence", "rights_holder", "kb_entry",
        "cite_key", "permissions_contact",
    ];

    public sealed record Recheck(string Field, string Value, string Outcome, string? RedirectTo);

    public sealed record UpdateSourceResult(
        string StatusBefore, string ReasonBefore, string Status, string Reason, int QuotedWords, int Limit, IReadOnlyList<Recheck>? Rechecks) : McpWriteResult;

    [McpServerTool(Name = "update_source", Title = "Fix the details of a source", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Changes a source: `fields` (kind, title, authors \"Family, Given; …\", year, publisher, edition, isbn, doi, url, accessed_on yyyy-MM-dd, " +
                 "licence, rights_holder, kb_entry, cite_key, permissions_contact; an empty string clears), `recheck` (isbn, doi, url: re-runs the identifier checks) " +
                 "and `accept_redirect` (replace the address a recheck says has moved). Returns the clearance before and after. Undo puts the fields back. " +
                 "Changing one use of a source (kind, locator, rewrap, remove) is update_source_use. To merge duplicates use merge_sources.")]
    public async Task<UpdateSourceResult> UpdateSourceAsync(
        McpServer server,
        [Description("The source: an id, a marker like \"#4\", a cite key or a title.")] string source,
        [Description("Fields to set, by name.")] JsonElement? fields = null,
        [Description("Identifiers to re-check: isbn, doi, url.")] IReadOnlyList<string>? recheck = null,
        [Description("Replace the address a recheck reported as moved with where it now lands.")] bool accept_redirect = false,
        [Description("Merge a duplicate into another source (use merge_sources instead).")] string? merge_into = null,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (merge_into != null)
        {
            throw McpToolException.Invalid("merge_into", "Merging sources is its own tool now: use merge_sources.");
        }
        var found = await sources.WritableAsync(source, book, cancellationToken);
        var sourceId = found.Row.Id;
        var before = await details.GetAsync(sourceId, cancellationToken);
        var wanted = fields is { ValueKind: JsonValueKind.Object } f ? f.EnumerateObject().ToList() : [];
        if (wanted.Select(p => p.Name).FirstOrDefault(n => !Fields.Contains(n, StringComparer.Ordinal)) is { } unknown)
        {
            throw McpToolException.Invalid("fields", $"\"{unknown}\" is not a source field: use {string.Join(", ", Fields)}.");
        }
        if ((recheck ?? []).FirstOrDefault(i => !Identifiers.Contains(i, StringComparer.Ordinal)) is { } badIdentifier)
        {
            throw McpToolException.Invalid("recheck", $"\"{badIdentifier}\" is not an identifier: use isbn, doi or url.");
        }
        if (wanted.Count == 0 && (recheck ?? []).Count == 0 && !accept_redirect)
        {
            throw McpToolException.Invalid("fields", "Nothing to change: give `fields`, `recheck` or `accept_redirect`.");
        }

        var caller = McpCaller.From(server);
        var steps = new List<McpUndoStep>();
        var effects = new List<string>();
        var affected = new List<McpAffected>();
        IReadOnlyList<Recheck>? rechecks = null;

        var extraFields = wanted.Where(p => p.Name is "cite_key" or "permissions_contact").ToList();
        var entryRequest = wanted.Any(p => p.Name == "kb_entry") ? await EntryAsync(found.Book, wanted.First(p => p.Name == "kb_entry").Value, cancellationToken) : null;
        var detailFields = wanted.Where(p => p.Name is not ("cite_key" or "permissions_contact")).ToList();
        var patching = detailFields.Count > 0 || accept_redirect || (recheck ?? []).Count > 0;
        if (accept_redirect)
        {
            await RequireRedirectAsync(detailFields.Count > 0 ? Overlay(before.Details, detailFields) : Request(before.Details), recheck, cancellationToken);
        }
        // The cite key and contact first: a taken cite key is the likely refusal, and it leaves nothing written. If the details are
        // then refused, the cite key and contact go back.
        SourceExtrasDto? extrasBefore = null;
        if (extraFields.Count > 0)
        {
            extrasBefore = await extras.GetAsync(sourceId, cancellationToken);
            var written = await extras.SetAsync(sourceId, new SourceExtrasRequest(ExtraText(extraFields, "cite_key"), ExtraText(extraFields, "permissions_contact")),
                cancellationToken, caller.Provenance);
            steps.Add(new McpUndoStep("set_source_extras", sourceId, Body: JsonSerializer.Serialize(extrasBefore, McpJson.Options),
                Now: new McpUndoState(Body: JsonSerializer.Serialize(written, McpJson.Options))));
        }
        if (detailFields.Count > 0)
        {
            try
            {
                await commands.PatchAsync(sourceId,
                    new PatchSourceRequest(detailFields.Any(p => p.Name != "kb_entry") ? Overlay(before.Details, detailFields) : null, entryRequest), cancellationToken, caller.Provenance);
            }
            catch (ApiException) when (extrasBefore != null)
            {
                await extras.SetAsync(sourceId, new SourceExtrasRequest(extrasBefore.CiteKey ?? "", extrasBefore.PermissionsContact ?? ""), cancellationToken, caller.Provenance);
                throw;
            }
        }
        if (wanted.Count > 0)
        {
            effects.Add($"Changed {string.Join(", ", wanted.Select(p => p.Name))} on {found.Item.Label}.");
            affected.AddRange(wanted.Select(p => new McpAffected("source", sourceId, p.Name, null, p.Value.ToString(), found.Item.Label)));
        }
        if ((recheck ?? []).Count > 0 || accept_redirect)
        {
            var result = await commands.RecheckAsync(sourceId, cancellationToken, caller.Provenance);
            rechecks = result.Fields.Where(r => (recheck ?? []).Count == 0 || (recheck ?? []).Contains(r.Field, StringComparer.Ordinal))
                .Select(r => new Recheck(r.Field, r.Value, r.Outcome, r.RedirectTo)).ToList();
            effects.AddRange(rechecks.Select(r => $"The {r.Field.ToUpperInvariant()} {Wording(r)}."));
        }
        if (accept_redirect)
        {
            var moved = (rechecks ?? []).FirstOrDefault(r => r.Outcome == "redirect")
                        ?? throw new InvalidOperationException("A redirect was found before anything was written, so the re-check must report it.");
            await commands.AcceptRedirectAsync(sourceId, new AcceptRedirectRequest(moved.RedirectTo!), cancellationToken, caller.Provenance);
            effects.Add($"The {moved.Field.ToUpperInvariant()} is now {moved.RedirectTo}.");
        }
        var after = await details.GetAsync(sourceId, cancellationToken);
        if (patching)
        {
            steps.Add(new McpUndoStep("patch_source", sourceId, Parent: before.Entry?.Id, Body: JsonSerializer.Serialize(Request(before.Details), McpJson.Options),
                Now: new McpUndoState(Source: State(after))));
        }
        if (before.Verdict.Status != after.Verdict.Status)
        {
            affected.Add(new McpAffected("source", sourceId, "status", before.Verdict.Status, after.Verdict.Status, found.Item.Label));
        }
        effects.Add($"Clearance: {before.Verdict.Status} → {after.Verdict.Status} ({McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit)}).");
        var undoId = await changeSets.RecordAsync(found.Book.Id, "update_source", $"Updated {found.Item.Label}", caller, steps, cancellationToken);
        return new UpdateSourceResult(before.Verdict.Status, McpClearance.Reason(before.Verdict.Reason, before.QuotedWords, before.Limit), after.Verdict.Status,
            McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit), after.QuotedWords, after.Limit, rechecks)
        {
            Item = new McpItem(sourceId, "source", McpSources.Label(after.Number, after.Title), CodexLinks.References(found.Book.Id, sourceId), after.Number is { } n ? $"#{n}" : null),
            UndoId = undoId, SideEffects = effects, Affected = affected,
        };
    }

    // accept_redirect needs a moved address, and only a re-check finds one. Look it up now, read-only, so that a call with nothing to accept
    // fails before any field is changed or any check is recorded. `recheck` narrows which identifiers count, as it does for the re-check.
    private async Task RequireRedirectAsync(SourceDetailsRequest details, IReadOnlyList<string>? recheck, CancellationToken ct)
    {
        var identifiers = new[] { (Field: SourceLookupKinds.Isbn, details.Isbn), (Field: SourceLookupKinds.Doi, details.Doi), (Field: SourceLookupKinds.Url, details.Url) }
            .Where(i => !string.IsNullOrWhiteSpace(i.Item2) && (recheck is not { Count: > 0 } || recheck.Contains(i.Field, StringComparer.Ordinal)));
        foreach (var (field, value) in identifiers)
        {
            if ((await lookup.LookupAsync(field, value!, ct)).Outcome == SourceLookupResult.RedirectOutcome)
            {
                return;
            }
        }
        throw McpToolException.Invalid("accept_redirect", "No re-check reported an address that has moved, so there is nothing to accept. Nothing was written.");
    }

    // A source's editable fields and Codex link as one string, for the undo step's precondition: undo only runs while the source still reads
    // what this write left.
    public static string State(SourceDetailDto d) => JsonSerializer.Serialize(new { Details = Request(d.Details), Entry = d.Entry?.Id }, McpJson.Options);

    // The editable fields as the Patch command takes them: the panel's own values, so an untouched field is rewritten as it was.
    public static SourceDetailsRequest Request(SourceDetailsDto d) =>
        new(d.Kind, d.Title, d.AuthorsText, d.Year, d.Publisher, d.Edition, d.Isbn, d.Doi, d.Url, d.AccessedOn, d.Licence, d.RightsHolder);

    private static SourceDetailsRequest Overlay(SourceDetailsDto d, IReadOnlyList<JsonProperty> set)
    {
        var r = Request(d);
        foreach (var p in set)
        {
            var text = Text(p.Value);
            r = p.Name switch
            {
                "kind" => r with { Kind = text },
                "title" => r with { Title = text },
                "authors" => r with { AuthorsText = text },
                "year" => r with { Year = Year(text) },
                "publisher" => r with { Publisher = text },
                "edition" => r with { Edition = text },
                "isbn" => r with { Isbn = text },
                "doi" => r with { Doi = text },
                "url" => r with { Url = text },
                "accessed_on" => r with { AccessedOn = text },
                "licence" => r with { Licence = text },
                "rights_holder" => r with { RightsHolder = text },
                _ => r,
            };
        }
        return r;
    }

    // The text asked for a field in `fields`, or null (leave it) when the field isn't there.
    private static string? ExtraText(IReadOnlyList<JsonProperty> fields, string name) =>
        fields.Any(p => p.Name == name) ? Text(fields.First(p => p.Name == name).Value) : null;

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.String => value.GetString() ?? "",
        _ => value.ToString(),
    };

    private static int? Year(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        return int.TryParse(text, out var year) ? year : throw McpToolException.Invalid("fields", "year is a number.");
    }

    private static string Wording(Recheck r) => r.Outcome switch
    {
        "ok" => "checks out",
        "redirect" => $"redirects to {r.RedirectTo}",
        _ => "was not found",
    };

    private async Task<SourceEntryRequest> EntryAsync(McpBook book, JsonElement value, CancellationToken ct)
    {
        var name = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new SourceEntryRequest(null);
        }
        return new SourceEntryRequest((await refs.EntryAsync(name, book, ct)).Id);
    }
}
