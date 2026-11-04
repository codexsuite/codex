using System.ComponentModel;
using Codex.Application.Features.References;
using Codex.Contracts;
using Codex.Contracts.References;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, the author's legal calls: record_clearance_decision (#20 section F). An adapter over
// ISourceCommands (Reopen) and IQuoteFlags (NotAQuote). Copyright decisions are UI-only over MCP (M6, confirmed by Joel
// 2026-10-22): sign_off and accept_fair_use never write. The agent prepares them (the uses and words, and where the author
// signs off) and gets `blocked` with rule `author_decision` and the open_url of the source in References. The rules are
// checked first, so the agent is never told to send the author to a sign-off Codex would refuse.
[McpServerToolType]
public sealed class DecisionTools(
    McpRefs refs, McpSources sources, ISourceCommands commands, ISourceDetailQueries details, IQuoteFlags flags, McpChangeSets changeSets,
    IBookAccess access)
{
    private static readonly string[] Decisions = ["sign_off", "accept_fair_use", "reopen", "not_a_quote"];

    public sealed record DecisionResult(string Decision, string StatusBefore, string Status, string Reason, int QuotedWords, int Limit, int OpenFlags) : McpWriteResult;

    [McpServerTool(Name = "record_clearance_decision", Title = "I've decided about this source", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Records a copyright decision as the author, with provenance 'agent'. `decision`: reopen takes back a sign-off; not_a_quote (with " +
                 "`flag`) dismisses an unflagged quote for good (its note is not stored). sign_off and accept_fair_use (needs `note`, the reason) are " +
                 "the author's legal calls and are made only in References: they fail blocked (rule author_decision) with the summary of uses and words " +
                 "and the open_url where the author signs off; nothing is written. Signing off a source that is still a copyright risk fails blocked with " +
                 "the reason in the words References uses. Undo cannot re-sign a source or take back a dismissed quote or fair-use call.")]
    public async Task<DecisionResult> RecordClearanceDecisionAsync(
        McpServer server,
        [Description("sign_off, accept_fair_use, reopen or not_a_quote.")] string decision,
        [Description("The source: an id, a marker like \"#4\", a cite key or a title.")] string? source = null,
        [Description("The unflagged quote (not_a_quote), from get_clearance.")] Guid? flag = null,
        [Description("The reason (accept_fair_use needs one); stored with accept_fair_use.")] string? note = null,
        [Description("The book, when `source` is a marker, cite key or title.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (!Decisions.Contains(decision, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("decision", $"\"{decision}\" is not a decision: use {string.Join(", ", Decisions)}.");
        }
        var caller = McpCaller.From(server);
        if (decision == "not_a_quote")
        {
            return await NotAQuoteAsync(flag, source, caller, cancellationToken);
        }
        if (source == null || flag != null)
        {
            throw McpToolException.Invalid("source", $"{decision} is about a source: give `source`, not `flag`.");
        }
        if (decision == "accept_fair_use" && string.IsNullOrWhiteSpace(note))
        {
            throw McpToolException.Invalid("note", "accept_fair_use needs a note: say why this is fair use.");
        }

        var found = await sources.WritableAsync(source, book, cancellationToken);
        var sourceId = found.Row.Id;
        var before = await details.GetAsync(sourceId, cancellationToken);
        var url = CodexLinks.References(found.Book.Id, sourceId);
        if (decision == "sign_off" && before.Verdict.Status == "risk")
        {
            throw new McpToolException(McpErrorCodes.Blocked,
                $"{Rule(before)} Get permission or cut the uses first, then the author signs off.") { Rule = "copyright_risk", OpenUrl = url };
        }
        if (decision is "sign_off" or "accept_fair_use")
        {
            var what = decision == "sign_off" ? $"sign off {found.Item.Label}" : $"accept {found.Item.Label} as fair use, because \"{note!.Trim()}\"";
            throw new McpToolException(McpErrorCodes.Blocked,
                $"Copyright decisions are the author's: they are made in References, not over MCP. Nothing was written. To {what}: {Summary(before)} The author decides at {url}.")
            { Rule = "author_decision", OpenUrl = url };
        }

        var signedOff = before.Verdict.SignedOffBy != null || before.Verdict.SignedOffLabel != null;
        await commands.ReopenAsync(sourceId, cancellationToken, caller.Provenance);
        McpUndoStep[] steps =
        [
            new McpUndoStep("unsupported", sourceId,
                Title: signedOff
                    ? "a sign-off is the author's call and can't be made over MCP; the author signs the source off again in References"
                    : "the source was not signed off"),
        ];
        var after = await details.GetAsync(sourceId, cancellationToken);
        var undoId = await changeSets.RecordAsync(found.Book.Id, "record_clearance_decision", $"{Past(decision)} {found.Item.Label}", caller, steps, cancellationToken);
        return new DecisionResult(decision, before.Verdict.Status, after.Verdict.Status, McpClearance.Reason(after.Verdict.Reason, after.QuotedWords, after.Limit),
            after.QuotedWords, after.Limit, 0)
        {
            Item = found.Item with { Label = McpSources.Label(after.Number, after.Title) }, UndoId = undoId,
            SideEffects = [$"{Past(decision)} {found.Item.Label} as the author. Clearance: {before.Verdict.Status} → {after.Verdict.Status}."],
            Affected = [new McpAffected("source", sourceId, "status", before.Verdict.Status, after.Verdict.Status, found.Item.Label)],
        };
    }

    private async Task<DecisionResult> NotAQuoteAsync(Guid? flag, string? source, McpCaller caller, CancellationToken ct)
    {
        if (flag is not { } flagId || source != null)
        {
            throw McpToolException.Invalid("flag", "not_a_quote is about an unflagged quote: give `flag` (from get_clearance), not `source`.");
        }
        var bookId = await sources.BookOfFlagAsync(flagId, ct) ?? throw McpToolException.NotFound($"No quote flag {flagId}.");
        var owner = await refs.BookAsync(bookId.ToString(), ct);
        await access.RequireAsync(owner.Id, BookRole.Write, ct);
        var remaining = await flags.NotAQuoteAsync(flagId, ct);
        var undoId = await changeSets.RecordAsync(owner.Id, "record_clearance_decision", "Dismissed an unflagged quote", caller,
            [new McpUndoStep("unsupported", flagId, Title: "a dismissed quote stays dismissed so it is never raised again; cite it from References if that was a mistake")], ct);
        return new DecisionResult("not_a_quote", "open", "dismissed", "Not a quote", 0, 0, remaining.Count)
        {
            Item = new McpItem(flagId, "quote_flag", "Unflagged quote", CodexLinks.References(owner.Id)), UndoId = undoId,
            SideEffects = ["The words are not a quote: they won't be flagged again.", $"{remaining.Count} unflagged quote{(remaining.Count == 1 ? "" : "s")} left to review."],
            Affected = [new McpAffected("quote_flag", flagId, "status", "open", "dismissed")],
        };
    }

    // "Quoted prose is over the limit: 342 of 300 words." and the like, in the panel's words.
    private static string Rule(SourceDetailDto d) => d.Verdict.Reason switch
    {
        "quote_over_limit" => $"Quoted prose is over the limit: {d.QuotedWords} of {d.Limit} words.",
        "lyric" => "Lyrics always need permission.",
        "figure" => "A figure needs permission or a licence that allows reuse.",
        "permission_drafted" or "permission_sent" or "permission_refused" => $"A copyright risk is open: {McpClearance.Reason(d.Verdict.Reason, d.QuotedWords, d.Limit)}.",
        _ => "A copyright risk is open on this source.",
    };

    // "214 of 300 quoted words, 1 paraphrase."
    private static string Summary(SourceDetailDto d)
    {
        var kinds = d.Uses.GroupBy(u => u.Kind).Select(g => $"{g.Count()} {g.Key}{(g.Count() == 1 ? "" : "s")}");
        return $"{d.QuotedWords} of {d.Limit} quoted words; {d.Uses.Count} use{(d.Uses.Count == 1 ? "" : "s")} ({string.Join(", ", kinds)}).";
    }

    private static string Past(string decision) => decision switch
    {
        "sign_off" => "Signed off",
        "accept_fair_use" => "Accepted as fair use",
        "reopen" => "Reopened",
        _ => "Decided",
    };
}
