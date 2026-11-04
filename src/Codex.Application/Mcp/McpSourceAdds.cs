using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.References;
using Codex.Contracts.References;
using Codex.Contracts.Sources;

namespace Codex.Application.Mcp;

// A source given by identifier or typed in, as cite_source and add_source take it (#20 section F): `{ isbn | doi | url }`
// is looked up through the Add-source commands (ISourceLookup, which refuses private addresses, M8) and matched against the
// book's sources, Not cited ones included; `{ new: { … } }` is added as typed. Answers how (matched or added) and the id of
// a source this call added, so the caller can archive it on undo or when its own write then fails.
[RegisterSingleton]
public sealed class McpSourceAdds(IAddSourceCommands adds)
{
    // `{ new: { … } }`: a source typed in. The fields are update_source's.
    public sealed record NewSource(
        string? Kind = null, string? Title = null, string? Authors = null, int? Year = null, string? Publisher = null, string? Edition = null,
        string? Isbn = null, string? Doi = null, string? Url = null, string? AccessedOn = null, string? Licence = null, string? RightsHolder = null);

    public sealed record Resolved(Guid? Id, string How, Guid? Created);

    // `element` is a JSON object. A dry run looks an identifier up (a match is reported) but adds nothing.
    public async Task<Resolved> ResolveAsync(McpBook owner, JsonElement element, bool forceNew, bool dryRun, McpCaller caller, CancellationToken ct)
    {
        if (element.TryGetProperty("new", out var typed))
        {
            var fields = typed.Deserialize<NewSource>(McpJson.Options) ?? throw McpToolException.Invalid("source", "{ new } needs the source's fields.");
            return await AddAsync(owner, Details(fields), null, dryRun, caller, ct);
        }
        foreach (var identifier in SourceLookupKinds.All)
        {
            if (!element.TryGetProperty(identifier, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var lookup = await adds.LookupAsync(owner.Id, new LookupSourceRequest(identifier, value.GetString() ?? ""), ct);
            if (lookup.Outcome == LookupSourceResultDto.ExistingOutcome && !forceNew)
            {
                return new Resolved(lookup.ExistingId, "matched", null);
            }
            if (lookup.Source is not { } found)
            {
                throw McpToolException.NotFound(lookup.Outcome == "redirect"
                    ? $"That {identifier} redirects to {lookup.RedirectTo}; give that address instead."
                    : $"No source found for that {identifier}. Give { "{ new: { … } }" } with its details.");
            }
            var request = new SourceDetailsRequest(identifier switch { "isbn" => "book", "doi" => "paper", _ => "web" }, found.Title,
                string.Join("; ", found.Authors), found.Year, found.Publisher, null,
                identifier == "isbn" ? lookup.Value : null, identifier == "doi" ? lookup.Value : null,
                identifier == "url" ? lookup.Value : found.Url, null, "unknown", null);
            return await AddAsync(owner, request, identifier, dryRun, caller, ct);
        }
        throw McpToolException.Invalid("source", "source needs `isbn`, `doi`, `url` or `new`.");
    }

    private async Task<Resolved> AddAsync(McpBook owner, SourceDetailsRequest request, string? via, bool dryRun, McpCaller caller, CancellationToken ct)
    {
        if (dryRun)
        {
            return new Resolved(null, "added", null);
        }
        var added = await adds.AddAsync(owner.Id, new AddSourceRequest(request, via), ct, caller.Provenance);
        if (added.Id is not { } id)
        {
            // The book already has this ISBN, DOI, URL or cite key: that source is the one (force_new cannot make a duplicate).
            return new Resolved(added.ExistingId, "matched", null);
        }
        return new Resolved(id, "added", id);
    }

    private static SourceDetailsRequest Details(NewSource f) =>
        new(f.Kind ?? "book", f.Title ?? "", f.Authors ?? "", f.Year, f.Publisher, f.Edition, f.Isbn, f.Doi, f.Url, f.AccessedOn, f.Licence ?? "unknown", f.RightsHolder);
}
