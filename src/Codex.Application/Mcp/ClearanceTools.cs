using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.References;
using Codex.Contracts.References;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. References and copyright, read side: get_clearance and export_references (#20 sections F and G), adapters over the
// References lane's queries (ISourceQueries, ISourceDetailQueries, IReferenceQueries, IQuoteFlags).
[McpServerToolType]
public sealed class ClearanceTools(
    McpRefs refs, McpSources sources, ISourceQueries sourceQueries, ISourceDetailQueries details, IReferenceQueries summaries, IQuoteFlags flags)
{
    private static readonly string[] Statuses = ["risk", "needs_check", "cleared", "not_cited"];
    private static readonly string[] Includes = ["uses", "checks", "permission", "history", "flags"];
    private static readonly string[] ExportFormats = ["bibliography", "bibtex", "ris", "csl_json", "permissions_log"];

    public sealed record Strip(int Cited, int Cleared, int NeedsCheck, int Risks, int NotCited, int Total, bool PrintBlocked, int UnflaggedQuotes);

    public sealed record SourceLine(
        McpItem Source, int? Marker, string Title, string Status, string Reason, string ReasonCode, string? NextAction, int Uses, string UsedIn,
        int QuotedWords, int Limit);

    public sealed record FlagLine(
        Guid Id, McpItem? Scene, string Reason, string Why, string Excerpt, McpItem? SourceGuess, int? MatchPercent, string? Locator);

    public sealed record UseLine(
        Guid Id, McpItem? Scene, string Kind, string Excerpt, int Words, string? Locator, bool NewSinceSignOff, bool Pending);

    public sealed record SourceDetail(
        string Kind, string Authors, int? Year, string? Publisher, string Licence, string? RightsHolder, int QuotedWords, int Limit, string? SignedOffBy,
        string? SignedOffLabel, IReadOnlyList<UseLine>? Uses, IReadOnlyList<ClearanceCheckDto>? Checks, PermissionDto? Permission,
        IReadOnlyList<SourceHistoryDto>? History);

    public sealed record GetClearanceResult(
        Strip? Summary, IReadOnlyList<SourceLine>? Sources, IReadOnlyList<FlagLine>? Flags, SourceLine? Focus, SourceDetail? Detail) : McpResult;

    public sealed record ExportReferencesResult(string Format, string FileName, string ContentType, int Entries, bool StyleApplied, string Content) : McpResult;

    [McpServerTool(Name = "get_clearance", Title = "What's blocking print?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Copyright clearance of a book. Without `source`: the summary strip (cited, cleared, risks, print_blocked, unflagged_quotes), every source " +
                 "with its marker number, status (risk, needs_check, cleared, not_cited), reason and next_action (reduce_quoted_words, request_permission, " +
                 "chase_permission, cut_or_request, sign_off, decide_fair_use, set_licence, fix_identifier, review_new_use, fill_details), and the " +
                 "unflagged-quote inbox (`flags`; pass cite_source `from_flag` to resolve one). With `source`: that source as the References panel shows it, " +
                 "uses (marking those new since sign-off), identifier checks, the permission request and history; `include` picks from uses, checks, " +
                 "permission, history and flags (default: the first four).")]
    public async Task<GetClearanceResult> GetClearanceAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\"; default the most recently edited.")] string? book = null,
        [Description("One source: an id, a marker like \"#4\", a cite key or a title.")] string? source = null,
        [Description("Only sources with these statuses: risk, needs_check, cleared, not_cited.")] IReadOnlyList<string>? status = null,
        [Description("What to include: uses, checks, permission, history, flags.")] IReadOnlyList<string>? include = null,
        CancellationToken cancellationToken = default)
    {
        if ((status ?? []).FirstOrDefault(s => !Statuses.Contains(s, StringComparer.Ordinal)) is { } badStatus)
        {
            throw McpToolException.Invalid("status", $"\"{badStatus}\" is not a status: use {string.Join(", ", Statuses)}.");
        }
        if ((include ?? []).FirstOrDefault(i => !Includes.Contains(i, StringComparer.Ordinal)) is { } badInclude)
        {
            throw McpToolException.Invalid("include", $"\"{badInclude}\" is not something to include: use {string.Join(", ", Includes)}.");
        }

        if (source != null)
        {
            var found = await sources.SourceAsync(source, book, cancellationToken);
            var outline = await refs.OutlineAsync(found.Book, McpRefs.SessionKey(server), cancellationToken);
            var want = include ?? ["uses", "checks", "permission", "history"];
            var detail = await details.GetAsync(found.Row.Id, cancellationToken);
            var open = want.Contains("flags") ? await flags.ListAsync(found.Book.Id, cancellationToken) : [];
            return new GetClearanceResult(null, null, want.Contains("flags") ? FlagLines(found.Book, outline, open, found.Row.Id) : null,
                Line(found.Book, found.Row),
                new SourceDetail(detail.KindLabel, detail.AuthorsLabel, detail.Year, detail.Publisher, detail.Details.Licence, detail.Details.RightsHolder,
                    detail.QuotedWords, detail.Limit, detail.Verdict.SignedOffBy, detail.Verdict.SignedOffLabel,
                    want.Contains("uses") ? detail.Uses.Select(u => new UseLine(u.Id, outline.SceneById(u.SceneId)?.Item(found.Book.Id), u.Kind, u.Excerpt, u.Words,
                        u.Locator, u.NewSinceSignOff, u.Pending)).ToList() : null,
                    want.Contains("checks") ? detail.Checks : null, want.Contains("permission") ? detail.Permission : null,
                    want.Contains("history") ? detail.History : null)) { Item = found.Item };
        }

        var owner = await refs.BookAsync(book, cancellationToken);
        var bookOutline = await refs.OutlineAsync(owner, McpRefs.SessionKey(server), cancellationToken);
        var rows = await sourceQueries.GetSourcesAsync(owner.Id, cancellationToken);
        var strip = await summaries.GetSummaryAsync(owner.Id, cancellationToken);
        var inbox = await flags.ListAsync(owner.Id, cancellationToken);
        var lines = rows.Where(r => status is not { Count: > 0 } || status.Contains(r.Status)).Select(r => Line(owner, r)).ToList();
        return new GetClearanceResult(
            new Strip(strip.Total - strip.NotCited, strip.Cleared, strip.NeedsCheck, strip.Risks, strip.NotCited, strip.Total, strip.Risks > 0, inbox.Count),
            lines, include is null || include.Contains("flags") ? FlagLines(owner, bookOutline, inbox, null) : null, null, null)
        {
            Item = owner.Item,
        };
    }

    [McpServerTool(Name = "export_references", Title = "Give me the bibliography", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A book's sources as text: `bibtex`, `ris`, `csl_json`, `permissions_log` (CSV) or `bibliography`. Cited sources only, unless " +
                 "include_uncited. `bibliography` is a plain author-year list: Codex formats entries in the book's citation style only in Print and the " +
                 "References tab, and the result says so (`style_applied: false`); for styled output take csl_json.")]
    public async Task<ExportReferencesResult> ExportReferencesAsync(
        [Description("The book: id, title or \"<series>/<number>\"; default the most recently edited.")] string? book = null,
        [Description("bibliography, bibtex, ris, csl_json or permissions_log.")] string format = "bibliography",
        [Description("Include sources nothing cites (default false).")] bool include_uncited = false,
        CancellationToken cancellationToken = default)
    {
        if (!ExportFormats.Contains(format, StringComparer.Ordinal))
        {
            throw McpToolException.Invalid("format", $"\"{format}\" is not a format: use {string.Join(", ", ExportFormats)}.");
        }
        var owner = await refs.BookAsync(book, cancellationToken);
        if (format == "permissions_log")
        {
            var file = await sourceQueries.ExportAsync(owner.Id, "permissions", cancellationToken);
            var text = System.Text.Encoding.UTF8.GetString(file.Content);
            return new ExportReferencesResult(format, file.FileName, file.ContentType, Math.Max(0, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1), true, text)
                { Item = owner.Item };
        }
        var rows = await sourceQueries.GetSourcesAsync(owner.Id, cancellationToken);
        var cited = include_uncited ? null : rows.Where(r => r.Cited).Select(r => r.Id).ToHashSet();
        var csl = include_uncited
            ? await AllCslAsync(owner.Id, cancellationToken)
            : (await sourceQueries.GetCslAsync(owner.Id, cancellationToken)).Where(c => cited!.Contains(c.SourceId)).Select(c => c.Csl).ToList();
        var name = $"{owner.Title}-sources";
        return format switch
        {
            "bibtex" => new ExportReferencesResult(format, $"{name}.bib", "application/x-bibtex", csl.Count, true, BibTexWriter.Write(csl)) { Item = owner.Item },
            "ris" => new ExportReferencesResult(format, $"{name}.ris", "application/x-research-info-systems", csl.Count, true, RisWriter.Write(csl)) { Item = owner.Item },
            "csl_json" => new ExportReferencesResult(format, $"{name}.json", "application/vnd.citationstyles.csl+json", csl.Count, true,
                JsonSerializer.Serialize(csl, new JsonSerializerOptions { WriteIndented = true })) { Item = owner.Item },
            _ => new ExportReferencesResult(format, $"{name}.txt", "text/plain", csl.Count, false, Bibliography(csl)) { Item = owner.Item },
        };
    }

    // Every live source as CSL, through the References lane's own export (so uncited ones are included).
    private async Task<IReadOnlyList<JsonElement>> AllCslAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var file = await sourceQueries.ExportAsync(bookId, "csljson", cancellationToken);
        return JsonSerializer.Deserialize<List<JsonElement>>(file.Content) ?? [];
    }

    // "Family, Given; Family, Given (Year). Title. Publisher." for each entry, in the order the sources are numbered.
    private static string Bibliography(IReadOnlyList<JsonElement> csl) =>
        string.Join('\n', csl.Select(item =>
        {
            var publisher = TextOf(item, "publisher");
            var names = Names(item);
            var byline = names.Count == 0 ? "" : string.Join("; ", names) + " ";
            return $"{byline}({YearOf(item)}). {TextOf(item, "title")}.{(publisher.Length == 0 ? "" : " " + publisher + ".")}";
        }));

    private static string TextOf(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static List<string> Names(JsonElement item)
    {
        if (!item.TryGetProperty("author", out var authors) || authors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return authors.EnumerateArray()
            .Select(a => a.TryGetProperty("literal", out var literal) ? literal.GetString() ?? "" : string.Join(", ", new[] { TextOf(a, "family"), TextOf(a, "given") }.Where(p => p.Length > 0)))
            .Where(n => n.Length > 0).ToList();
    }

    private static string YearOf(JsonElement item)
    {
        if (item.TryGetProperty("issued", out var issued) && issued.TryGetProperty("date-parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 && parts[0].ValueKind == JsonValueKind.Array && parts[0].GetArrayLength() > 0)
        {
            return parts[0][0].ToString();
        }
        return "n.d.";
    }

    private static SourceLine Line(McpBook book, SourceRowDto r) =>
        new(McpSources.ItemOf(book, r), r.Number, r.Title, r.Status, McpClearance.Reason(r.Reason, r.QuotedWords, r.Limit), r.Reason,
            McpClearance.NextAction(r.Status, r.Reason), r.Uses, r.UsedIn, r.QuotedWords, r.Limit);

    private static List<FlagLine> FlagLines(McpBook book, BookOutline outline, IReadOnlyList<QuoteFlagDto> open, Guid? guess) =>
        open.Where(f => guess == null || f.ReferenceId == guess)
            .Select(f => new FlagLine(f.Id, outline.SceneById(f.SceneId)?.Item(book.Id), f.Reason, f.Why, f.Excerpt,
                f.ReferenceId is { } id ? new McpItem(id, "source", f.ReferenceLabel ?? "", CodexLinks.References(book.Id, id)) : null, f.MatchPercent, f.Locator))
            .ToList();
}
