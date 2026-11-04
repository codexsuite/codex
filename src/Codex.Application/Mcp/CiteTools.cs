using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.Citations;
using Codex.Application.Features.References;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, citing: cite_source (#20 section F), an adapter over the Cite lane's ICitationCommands
// (CitePassage, CiteFlag) and McpSourceAdds. Cites words already in a scene, or an open unflagged-quote flag,
// for a source named by ref, by identifier (looked up through ISourceLookup, which refuses private addresses, M8) or
// typed in. One call records one change set; a citation of a typed-in or looked-up source also archives that source on
// undo, and a citation from a flag cannot be undone over MCP (the flag stays resolved).
[McpServerToolType]
public sealed class CiteTools(
    McpRefs refs, McpScenes scenes, McpSources sources, ICitationCommands citations, McpSourceAdds adds, IQuoteFlags flags, ISourceCommands sourceCommands,
    ISourceDetailQueries details, IProseStore prose, McpChangeSets changeSets, IBookAccess access)
{
    private static readonly string[] Kinds = ["quote", "paraphrase", "data", "figure", "lyric"];

    public sealed record CiteResult(
        Guid Use, int Marker, int MarkersShifted, string Kind, string? Locator, McpItem Source, McpItem? Scene, string SourceHow,
        string StatusBefore, string Status, string Reason, string ReasonCode, bool DryRun) : McpWriteResult;

    [McpServerTool(Name = "cite_source", Title = "Cite this", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Cites a source for words already in a scene: `scene` + `passage` { text, occurrence? } + expected_version (from read_manuscript), or " +
                 "`from_flag` (an unflagged-quote id from get_clearance; its words are cited, with the flag's own locator). `source` is a ref (an id, " +
                 "\"#4\", a cite key or a title), `{ isbn | doi | url }` (looked up and matched against the book's sources, including Not cited ones; " +
                 "a new one is added if none matches) or `{ new: { kind, title, authors, year, publisher, … } }`. Returns the use, its marker number, " +
                 "how many later markers shifted, and the source's clearance before and after. kind: quote, paraphrase (default), data, figure, lyric. " +
                 "A stale expected_version fails with conflict. dry_run resolves the source and reports without writing. Undo restores the scene's prose " +
                 "(and archives a source this call added); a citation from a flag cannot be undone here.")]
    public async Task<CiteResult> CiteSourceAsync(
        McpServer server,
        [Description("The scene: an id, or \"7.1\" with `book`.")] string? scene = null,
        [Description("The words to wrap: { text, occurrence? }, exactly as stored in the prose.")] ProseTools.Anchor? passage = null,
        [Description("A source ref, { isbn | doi | url }, or { new: { … } }.")] JsonElement? source = null,
        [Description("quote, paraphrase (default), data, figure or lyric.")] string? kind = null,
        [Description("Where in the source: \"pp. 41–43\", \"Table 5.1\".")] string? locator = null,
        [Description("Cite an open unflagged quote instead of a passage.")] Guid? from_flag = null,
        [Description("Add a new source even when one matches the identifier.")] bool force_new = false,
        [Description("The scene's version from read_manuscript.")] int? expected_version = null,
        [Description("The book, when `scene` or `source` is a display number or title.")] string? book = null,
        [Description("Report the result without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (kind != null && !Kinds.Contains(kind, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("kind", $"\"{kind}\" is not a kind: use {string.Join(", ", Kinds)}.");
        }
        if (from_flag == null && (scene == null || passage == null || expected_version == null))
        {
            throw McpToolException.Invalid("scene", "Give `scene`, `passage` and `expected_version`, or `from_flag`.");
        }
        if (from_flag != null && (scene != null || passage != null))
        {
            throw McpToolException.Invalid("from_flag", "`from_flag` cites the flag's own words: leave `scene` and `passage` out.");
        }

        var caller = McpCaller.From(server);
        McpBook owner;
        McpScenes.Target? target = null;
        QuoteFlagDto? flag = null;
        if (from_flag is { } flagId)
        {
            var bookId = await sources.BookOfFlagAsync(flagId, cancellationToken) ?? throw McpToolException.NotFound($"No quote flag {flagId}.");
            owner = await refs.BookAsync(bookId.ToString(), cancellationToken);
            flag = (await flags.ListAsync(owner.Id, cancellationToken)).FirstOrDefault(f => f.Id == flagId)
                   ?? throw new McpToolException(McpErrorCodes.Conflict, "That quote has already been resolved.");
            if (locator != null && !string.Equals(locator.Trim(), flag.Locator, StringComparison.Ordinal))
            {
                throw McpToolException.Invalid("locator", $"A citation from a flag uses the flag's own locator ({flag.Locator ?? "none"}); cite the passage with `scene` and `passage` to set another.");
            }
        }
        else
        {
            target = await scenes.SceneAsync(server, book, scene!, cancellationToken);
            owner = target.Outline.Book;
        }
        await access.RequireAsync(owner.Id, BookRole.Write, cancellationToken);

        var (sourceId, how, created) = await ResolveAsync(owner, source, flag, force_new, dry_run, caller, cancellationToken);
        var sceneId = target?.Scene.Dto.Id ?? flag!.SceneId;
        if (dry_run)
        {
            var existing = sourceId is { } id ? (await sources.SourceAsync(id.ToString(), null, cancellationToken)).Item : null;
            return new CiteResult(Guid.Empty, 0, 0, kind ?? (flag != null ? "quote" : "paraphrase"), locator ?? flag?.Locator,
                existing ?? new McpItem(Guid.Empty, "source", "(a new source)", CodexLinks.References(owner.Id)), target?.Item, how, "", "", "", "", true)
            {
                Item = existing, SideEffects = [$"Would cite {existing?.Label ?? "a new source"} in the scene; nothing was written."],
            };
        }

        var before = (await prose.GetAsync(sceneId, cancellationToken)).Body;
        CitePassageResult result;
        try
        {
            result = flag != null
                ? await citations.CiteFlagAsync(flag.Id, new CiteFlagRequest(sourceId, kind), ProseTools.Origin, cancellationToken)
                : await citations.CitePassageAsync(
                    new CitePassageRequest(sceneId, sourceId!.Value, expected_version!.Value, passage!.Text, passage.Occurrence, kind, locator), ProseTools.Origin, cancellationToken);
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict && flag == null)
        {
            await DiscardAsync(created, caller, cancellationToken);
            var now = (await prose.GetAsync(sceneId, cancellationToken)).Version;
            throw new McpToolException(McpErrorCodes.Conflict,
                $"The scene is at version {now}, not {expected_version}: read it again before citing. {e.Message}");
        }
        catch (ApiException)
        {
            await DiscardAsync(created, caller, cancellationToken);
            throw;
        }

        var sourceKey = await details.GetAsync(sourceId ?? throw new InvalidOperationException("A citation has a source."), cancellationToken);
        await changeSets.CutAgentRevisionAsync(sceneId, before, caller, cancellationToken);
        var rows = await sources.SourceAsync(sourceKey.Id.ToString(), owner.Id.ToString(), cancellationToken);
        List<McpUndoStep> steps = [];
        if (flag != null)
        {
            steps.Add(new McpUndoStep("unsupported", result.UseId,
                Title: "a citation made from a quote flag resolves the flag, which can't be reopened over MCP"));
        }
        else
        {
            steps.Add(new McpUndoStep("prose", sceneId, Body: before, Version: result.Version));
            if (created is { } newId)
            {
                steps.Add(new McpUndoStep("archive_source", newId));
            }
        }
        var sceneItem = (await refs.OutlineAsync(owner, McpRefs.SessionKey(server), cancellationToken)).SceneById(sceneId)?.Item(owner.Id);
        var undoId = await changeSets.RecordAsync(owner.Id, "cite_source", $"Cited {rows.Item.Label}", caller, steps, cancellationToken);
        var sinceSignOff = sourceKey.Uses.Count(u => u.NewSinceSignOff);
        var effects = new List<string> { $"{rows.Item.Label} is marker {result.Number} in {sceneItem?.Label ?? "the scene"}." };
        if (result.MarkersShifted > 0)
        {
            effects.Add($"{result.MarkersShifted} later marker{(result.MarkersShifted == 1 ? "" : "s")} moved up by one.");
        }
        if (how != "ref")
        {
            effects.Add(how switch
            {
                "added" => $"{rows.Item.Label} was added to the book's sources.",
                _ => $"The identifier matched {rows.Item.Label}, already in the book's sources; it was used.",
            });
        }
        if (result.Status == "needs_check" && sinceSignOff > 0 && sourceKey.Verdict.SignedOffLabel != null)
        {
            effects.Add($"{rows.Item.Label} now has {sinceSignOff} use{(sinceSignOff == 1 ? "" : "s")} since sign-off.");
        }
        effects.Add($"Clearance: {result.StatusBefore} → {result.Status} ({McpClearance.Reason(result.Reason, rows.Row.QuotedWords, rows.Row.Limit)}).");
        var written = kind ?? (flag != null ? "quote" : "paraphrase");
        return new CiteResult(result.UseId, result.Number, result.MarkersShifted, written,
            locator ?? flag?.Locator, rows.Item, sceneItem, how, result.StatusBefore, result.Status,
            McpClearance.Reason(result.Reason, rows.Row.QuotedWords, rows.Row.Limit), result.Reason, false)
        {
            Item = rows.Item, Version = result.Version, UndoId = undoId, SideEffects = effects,
            Affected =
            [
                new McpAffected("source_use", result.UseId, "created", null, written),
                new McpAffected("source", sourceKey.Id, "status", result.StatusBefore, result.Status, rows.Item.Label),
            ],
        };
    }

    // The source to cite: how (ref, matched or added), and the id of the source this call added, if it did.
    private async Task<(Guid? Id, string How, Guid? Created)> ResolveAsync(
        McpBook owner, JsonElement? source, QuoteFlagDto? flag, bool forceNew, bool dryRun, McpCaller caller, CancellationToken ct)
    {
        if (source is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return flag?.ReferenceId is { } guess
                ? (guess, "ref", null)
                : throw McpToolException.Invalid("source", "Say which source: the flag has no best guess.");
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            return ((await sources.SourceAsync(element.GetString() ?? "", owner.Id.ToString(), ct)).Row.Id, "ref", null);
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw McpToolException.Invalid("source", "source is a ref string, { isbn | doi | url } or { new: { … } }.");
        }

        var resolved = await adds.ResolveAsync(owner, element, forceNew, dryRun, caller, ct);
        return (resolved.Id, resolved.How, resolved.Created);
    }

    // A source this call added, when the citation then failed: archive it, so a refused call leaves nothing behind.
    private async Task DiscardAsync(Guid? created, McpCaller caller, CancellationToken ct)
    {
        if (created is { } id)
        {
            await sourceCommands.ArchiveAsync(id, ct, caller.Provenance);
        }
    }
}
