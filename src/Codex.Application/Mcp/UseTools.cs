using System.ComponentModel;
using Codex.Application.Features.Citations;
using Codex.Application.Features.References;
using Codex.Application.Prose;
using Codex.Contracts.Citations;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, fixing one use of a source: update_source_use (#20 section F, M1, M10, M11). An adapter over
// the References lane's ISourceCommands.SetUseKindAsync and IUseCommands.SetLocatorAsync, and the Cite lane's
// ICitationCommands (RewrapAsync, RemoveAsync). `rewrap` keeps the same reference_use and wraps new words in the scene
// with it, the move Paraphrase with AI makes once the words are rewritten. Arguments are checked before anything is
// written. One change set: kind and locator go back on undo, and so does a rewrap (the scene's prose); removing a use
// cannot be undone over MCP.
[McpServerToolType]
public sealed class UseTools(
    McpRefs refs, McpSources sources, ISourceCommands commands, IUseCommands uses, ISourceDetailQueries details, ICitationCommands citations,
    IProseStore prose, McpChangeSets changeSets)
{
    private static readonly string[] UseKinds = ["quote", "paraphrase", "data", "figure", "lyric"];

    public sealed record UpdateSourceUseResult(
        Guid Use, string Kind, string? Locator, McpItem Source, McpItem? Scene, string StatusBefore, string Status, string Reason, int QuotedWords, int Limit,
        bool DryRun) : McpWriteResult;

    [McpServerTool(Name = "update_source_use", Title = "Fix one use of a source", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Changes one use of a source (its id is in get_clearance): `kind` (quote, paraphrase, data, figure, lyric), `locator` (where in the source; " +
                 "empty clears), `rewrap` { text, occurrence? } + expected_version (moves the use's marker onto the words you just wrote in its scene: " +
                 "the same use, so its kind, locator and history stay, and the old words become plain text), or `remove` (unwraps the marker, which " +
                 "archives the use; expected_version optional). Returns the source's clearance before and after. A stale expected_version fails with " +
                 "conflict; a `rewrap` text that is not in the scene is invalid. dry_run reports without writing. Undo puts kind, locator and a rewrap's " +
                 "prose back while they still read what the call left; removing a use cannot be undone here.")]
    public async Task<UpdateSourceUseResult> UpdateSourceUseAsync(
        McpServer server,
        [Description("The use: its id, from get_clearance.")] Guid use,
        [Description("quote, paraphrase, data, figure or lyric.")] string? kind = null,
        [Description("Where in the source: \"pp. 41–43\", \"Table 5.1\"; an empty string clears it.")] string? locator = null,
        [Description("The new words to carry the marker: { text, occurrence? }, exactly as stored in the prose.")] ProseTools.Anchor? rewrap = null,
        [Description("Unwrap the use's marker in the prose, which archives the use.")] bool remove = false,
        [Description("The scene's version from read_manuscript (required with `rewrap`).")] int? expected_version = null,
        [Description("Report the result without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        if (kind != null && !UseKinds.Contains(kind, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("kind", $"\"{kind}\" is not a use kind: use {string.Join(", ", UseKinds)}.");
        }
        if (remove && (kind != null || locator != null || rewrap != null))
        {
            throw McpToolException.Invalid("remove", "A use is either removed or changed, not both: leave `kind`, `locator` and `rewrap` out.");
        }
        if (kind == null && locator == null && rewrap == null && !remove)
        {
            throw McpToolException.Invalid("kind", "Nothing to change: give `kind`, `locator`, `rewrap` or `remove`.");
        }
        if (rewrap != null && expected_version == null)
        {
            throw McpToolException.Invalid("expected_version", "`rewrap` changes the scene's prose: give expected_version from read_manuscript.");
        }
        if (rewrap != null && string.IsNullOrWhiteSpace(rewrap.Text))
        {
            throw McpToolException.Invalid("rewrap", "`rewrap` needs the new words in `text`.");
        }

        var info = await uses.GetAsync(use, cancellationToken);
        if (info.Archived && rewrap == null)
        {
            throw McpToolException.NotFound($"Use {use} is archived: its marker is not in the prose any more. Give `rewrap` to put it on new words.");
        }
        var found = await sources.WritableAsync(info.SourceId.ToString(), info.BookId.ToString(), cancellationToken);
        var caller = McpCaller.From(server);
        var before = await details.GetAsync(info.SourceId, cancellationToken);
        var book = found.Book;
        var scene = (await refs.OutlineAsync(book, McpRefs.SessionKey(server), cancellationToken)).SceneById(info.SceneId)?.Item(book.Id);
        var where = scene?.Label ?? "the scene";
        var effects = new List<string>();
        var affected = new List<McpAffected>();
        McpUndoStep? proseStep = null;
        int? version = null;

        if (dry_run)
        {
            if (rewrap != null)
            {
                await CheckRewrapAsync(info, rewrap, expected_version!.Value, cancellationToken);
                effects.Add($"Would move the marker of {found.Item.Label} in {where} onto \"{rewrap.Text}\"; nothing was written.");
            }
            if (remove)
            {
                effects.Add($"Would unwrap the marker of {found.Item.Label} in {where}; nothing was written.");
            }
            if (kind != null && kind != info.Kind)
            {
                effects.Add($"Would make the use a {kind}, not a {info.Kind}.");
            }
            if (locator != null)
            {
                effects.Add($"Would set the locator to {(string.IsNullOrWhiteSpace(locator) ? "none" : locator.Trim())}.");
            }
            return Result(info, kind ?? info.Kind, locator == null ? info.Locator : Clean(locator), found, scene, before, before, true, null, null, effects, affected);
        }

        if (remove)
        {
            var current = await prose.GetAsync(info.SceneId, cancellationToken);
            var removed = await citations.RemoveAsync(use, new RemoveCitationRequest(expected_version ?? current.Version), ProseTools.Origin, cancellationToken);
            await changeSets.CutAgentRevisionAsync(info.SceneId, current.Body, caller, cancellationToken);
            version = removed.Version;
            proseStep = new McpUndoStep("unsupported", use, Title: "a removed citation can't be put back over MCP; cite the passage again");
            effects.Add($"Removed the marker of a use in {where}: the words stay as plain text.");
            affected.Add(new McpAffected("source_use", use, "archived", "false", "true"));
        }

        if (rewrap != null)
        {
            var current = await prose.GetAsync(info.SceneId, cancellationToken);
            CitePassageResult result;
            try
            {
                result = await citations.RewrapAsync(use, new RewrapCitationRequest(expected_version!.Value, rewrap.Text, rewrap.Occurrence), ProseTools.Origin, cancellationToken);
            }
            catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
            {
                throw new McpToolException(McpErrorCodes.Conflict,
                    $"The scene is at version {current.Version}, not {expected_version}: read it again before rewrapping. {e.Message}");
            }
            await changeSets.CutAgentRevisionAsync(info.SceneId, current.Body, caller, cancellationToken);
            version = result.Version;
            proseStep = new McpUndoStep("prose", info.SceneId, Body: current.Body, Version: result.Version);
            effects.Add($"The marker of {found.Item.Label} in {where} now wraps the new words; the old words are plain text.");
            affected.Add(new McpAffected("source_use", use, "words", null, rewrap.Text));
        }

        McpUndoStep? kindStep = null;
        if (kind != null && kind != info.Kind)
        {
            await commands.SetUseKindAsync(use, new PatchUseRequest(kind), cancellationToken, caller.Provenance);
            kindStep = new McpUndoStep("set_use_kind", use, Title: info.Kind, Now: new McpUndoState(Kind: kind));
            effects.Add($"The use in {where} is now a {kind}, not a {info.Kind}.");
            affected.Add(new McpAffected("source_use", use, "kind", info.Kind, kind));
        }

        McpUndoStep? locatorStep = null;
        var wanted = locator == null ? info.Locator : Clean(locator);
        if (locator != null && !string.Equals(wanted, info.Locator, StringComparison.Ordinal))
        {
            await uses.SetLocatorAsync(use, new SetUseLocatorRequest(locator), cancellationToken, caller.Provenance);
            locatorStep = new McpUndoStep("set_use_locator", use, Title: info.Locator ?? "", Now: new McpUndoState(Body: wanted ?? ""));
            effects.Add($"The locator of the use in {where} is now {wanted ?? "none"}.");
            affected.Add(new McpAffected("source_use", use, "locator", info.Locator, wanted));
        }

        var after = await details.GetAsync(info.SourceId, cancellationToken);
        if (before.Verdict.Status != after.Verdict.Status)
        {
            affected.Add(new McpAffected("source", info.SourceId, "status", before.Verdict.Status, after.Verdict.Status, found.Item.Label));
        }
        effects.Add($"Clearance: {before.Verdict.Status} → {after.Verdict.Status} ({McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit)}).");
        // Undo runs the steps in this order: the later writes first, the prose last (putting the old prose back may archive the use again).
        McpUndoStep[] steps = new[] { locatorStep, kindStep, proseStep }.OfType<McpUndoStep>().ToArray();
        var undoId = steps.Length == 0 ? null : await changeSets.RecordAsync(book.Id, "update_source_use", $"Updated a use of {found.Item.Label}", caller, steps, cancellationToken);
        return Result(info, kind ?? info.Kind, wanted, found, scene, before, after, false, version, undoId, effects, affected);
    }

    private static string? Clean(string locator) => string.IsNullOrWhiteSpace(locator) ? null : locator.Trim();

    private UpdateSourceUseResult Result(
        UseInfoDto info, string kind, string? locator, McpSources.Source found, McpItem? scene, SourceDetailDto before, SourceDetailDto after, bool dryRun,
        int? version, string? undoId, IReadOnlyList<string> effects, IReadOnlyList<McpAffected> affected) =>
        new(info.Id, kind, locator, found.Item, scene, before.Verdict.Status, after.Verdict.Status, McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit),
            after.QuotedWords, after.Limit, dryRun)
        {
            Item = found.Item, Version = version, UndoId = undoId, SideEffects = effects, Affected = affected,
        };

    // dry_run for rewrap: the version is current and the words are in the scene's prose (with the use's old marker taken off).
    private async Task CheckRewrapAsync(UseInfoDto info, ProseTools.Anchor rewrap, int expectedVersion, CancellationToken ct)
    {
        var current = await prose.GetAsync(info.SceneId, ct);
        if (current.Version != expectedVersion)
        {
            throw new McpToolException(McpErrorCodes.Conflict, $"The scene is at version {current.Version}, not {expectedVersion}: read it again before rewrapping.");
        }
        var text = current.Body;
        try
        {
            text = ProseDocument.Parse(current.Body).UnwrapRef(info.Id).Text;
        }
        catch (ProseEditException e) when (e.Failure == ProseEditFailure.TargetNotFound)
        {
            // An archived use has no marker to take off.
        }
        var at = -1;
        for (var n = 0; n < (rewrap.Occurrence ?? 1); n++)
        {
            at = text.IndexOf(rewrap.Text, at + 1, StringComparison.Ordinal);
            if (at < 0)
            {
                throw McpToolException.Invalid("rewrap", "The words are not in the scene's prose (occurrence counted with the use's old marker taken off).");
            }
        }
    }
}
