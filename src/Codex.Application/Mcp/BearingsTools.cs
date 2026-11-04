using System.ComponentModel;
using Codex.Application.Features.KnowledgeBase;
using Codex.Application.Features.Manuscript;
using Codex.Application.Features.References;
using Codex.Application.Features.Shell;
using Codex.Contracts.Structure;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// A. Get my bearings: resume_work and get_outline (#20, M2). Adapters over OutlineQuery (through McpRefs), the
// Write screen's reads (ManuscriptContexts), the book header and the References summary.
[McpServerToolType]
public sealed class BearingsTools(
    McpRefs refs, McpQueries queries, ManuscriptContexts contexts, IShellQueries shell, IReferenceQueries references, IQuoteFlags flags,
    IKbQueries kb, BlockCommands blocks)
{
    private const string NeedsResearch = "Needs research";
    private static readonly string[] Includes = ["summaries", "labels", "word_counts", "kb_links", "beats", "archived", "kb_index", "plan_notes"];
    private static readonly string[] DefaultIncludes = ["summaries", "labels", "word_counts"];

    public sealed record BookBrief(McpItem Item, string LastEdited);

    public sealed record LastScene(
        McpItem Item, IReadOnlyList<string> Labels, int Words, int Version, ReadTools.DraftView? PendingDraft);

    public sealed record Attention(McpItem Item, string Why);

    public sealed record ReferencesBrief(McpItem Item, int Risks, int NeedsCheck, int Cleared, int UnflaggedQuotes, bool PrintBlocked);

    public sealed record ResumeWorkResult(
        BookBrief Book, LastScene? LastScene, IReadOnlyList<PendingDraft> PendingDrafts, IReadOnlyList<Attention> NeedsAttention,
        ReferencesBrief References) : McpResult;

    public sealed record PendingDraft(ReadTools.DraftView Draft, McpItem Scene, int SceneVersion);

    public sealed record BeatLine(Guid Id, string Instruction, int? TargetWords, bool Hidden);

    public sealed record SceneLine(
        McpItem Item, string? Title, string? Summary, string Status, int? Words, bool PendingDraft, IReadOnlyList<string>? Labels,
        IReadOnlyList<McpItem>? KbLinks, IReadOnlyList<BeatLine>? Beats, string? PlanNotes = null);

    public sealed record ChapterLine(McpItem Item, string Slug, int ArchivedScenes, IReadOnlyList<SceneLine> Scenes,
        string? Summary = null, string? PlanNotes = null);

    public sealed record ActLine(McpItem Item, IReadOnlyList<ChapterLine> Chapters);

    public sealed record KbCategoryLine(Guid Id, string Name, string Scope, string? SeriesName, int Entries);

    public sealed record GetOutlineResult(
        IReadOnlyList<ActLine> Acts, IReadOnlyList<ChapterLine>? ArchivedChapters, IReadOnlyList<KbCategoryLine>? KbIndex) : McpResult;

    [McpServerTool(Name = "resume_work", Title = "Pick up where I left off", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A briefing on a book: the scene last worked on (with its version and pending AI draft), every pending draft with its text, " +
                 "scenes waiting on the author (Needs research, or no summary and no prose) and the References risks. Each line has an open_url.")]
    public async Task<ResumeWorkResult> ResumeWorkAsync(
        McpServer server,
        [Description("The book; default the most recently edited one.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var target = await refs.BookAsync(book, cancellationToken);
        var outline = await refs.OutlineAsync(target, McpRefs.SessionKey(server), cancellationToken);
        var header = await shell.GetBookHeaderAsync(target.Id, cancellationToken);
        var context = await contexts.ForBookAsync(target.Id, cancellationToken);

        var pending = new List<PendingDraft>();
        foreach (var draft in context.Drafts)
        {
            if (outline.SceneById(draft.SceneId) is { } scene)
            {
                var item = scene.Item(target.Id);
                pending.Add(new PendingDraft(ReadTools.View(draft, item, context), item,
                    (await queries.SceneProseAsync(scene.Dto.Id, cancellationToken)).Version));
            }
        }

        LastScene? last = null;
        if (await queries.LastSceneAsync(target.Id, cancellationToken) is { } lastId && outline.SceneById(lastId) is { } lastScene)
        {
            var item = lastScene.Item(target.Id);
            last = new LastScene(item, lastScene.Dto.Labels.Select(l => l.Name).ToList(), lastScene.Dto.Words,
                (await queries.SceneProseAsync(lastId, cancellationToken)).Version,
                pending.FirstOrDefault(p => p.Scene.Id == lastId)?.Draft);
        }

        var attention = outline.Scenes
            .Where(s => s.Dto.Id != last?.Item.Id)
            .SelectMany(s => WaitingReason(s.Dto, context.Beats.Values.Any(b => b.SceneId == s.Dto.Id)) is { } why
                ? [new Attention(s.Item(target.Id), why)]
                : Array.Empty<Attention>())
            .ToList();

        var summary = await references.GetSummaryAsync(target.Id, cancellationToken);
        var quotes = await flags.ListAsync(target.Id, cancellationToken);
        return new ResumeWorkResult(
            new BookBrief(target.Item with { OpenUrl = CodexLinks.Write(target.Id) }, header.LastEditedLabel),
            last, pending, attention,
            new ReferencesBrief(new McpItem(target.Id, "references", $"{target.Title} · References", CodexLinks.References(target.Id)),
                summary.Risks, summary.NeedsCheck, summary.Cleared, quotes.Count, summary.Risks > 0))
        {
            Item = target.Item,
        };
    }

    [McpServerTool(Name = "get_outline", Title = "Show me the shape of the book", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Acts → chapters → scenes in book order, with display numbers, ids, titles, labels, status, words and a pending_draft flag. " +
                 "include adds kb_links, beats, plan_notes (each chapter's and scene's planning notes; summaries likewise covers chapters), archived (archived chapters) or kb_index (the Knowledge base categories in scope, book or series).")]
    public async Task<GetOutlineResult> GetOutlineAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("One act or chapter instead of the whole book: \"act:2\", \"5\", \"ch:<slug>\" or an id.")] string? within = null,
        [Description("Any of summaries, labels, word_counts (the default three), kb_links, beats, plan_notes, archived, kb_index.")] string[]? include = null,
        CancellationToken cancellationToken = default)
    {
        var parts = include is { Length: > 0 } ? include : DefaultIncludes;
        if (parts.FirstOrDefault(p => !Includes.Contains(p)) is { } unknown)
        {
            throw McpToolException.Invalid("include", $"\"{unknown}\" is not one of {string.Join(", ", Includes)}.");
        }
        var target = await refs.BookAsync(book, cancellationToken);
        var outline = await refs.OutlineAsync(target, McpRefs.SessionKey(server), cancellationToken);
        var scope = within == null ? null : McpRefs.Item(outline, within);
        if (scope is { Kind: "scene" })
        {
            throw McpToolException.Invalid("within", "within takes an act or a chapter; read a scene with read_manuscript.");
        }
        var beats = parts.Contains("beats")
            ? (await blocks.ListAsync(target.Id, cancellationToken)).Beats.ToLookup(b => b.SceneId)
            : null;

        var planNotes = parts.Contains("plan_notes") ? await queries.ScenePlanNotesAsync(target.Id, cancellationToken) : null;

        SceneLine Scene(BookOutline.Scene s) => new(
            s.Item(target.Id), s.Dto.Title, parts.Contains("summaries") ? s.Dto.Summary : null, s.Dto.Status,
            parts.Contains("word_counts") ? s.Dto.Words : null, s.Dto.HasPendingDraft,
            parts.Contains("labels") ? s.Dto.Labels.Select(l => l.Name).ToList() : null,
            parts.Contains("kb_links")
                ? s.Dto.LinkedEntries.Select(e => new McpItem(e.Id, "kb_entry", e.Name, CodexLinks.Plan(target.Id, entry: e.Id))).ToList()
                : null,
            beats?[s.Dto.Id].Select(b => new BeatLine(b.Id, b.Body, b.TargetWordCount, b.IsHidden)).ToList(),
            planNotes?.GetValueOrDefault(s.Dto.Id));

        var chapterTexts = parts.Contains("summaries") || parts.Contains("plan_notes")
            ? await queries.ChapterTextsAsync(target.Id, cancellationToken)
            : null;

        ChapterLine Chapter(BookOutline.Chapter c) => new(
            c.Item(target.Id), c.Dto.Slug, c.Dto.ArchivedSceneCount, c.Dto.Scenes.Select(s => Scene(new BookOutline.Scene(c, s))).ToList(),
            parts.Contains("summaries") ? chapterTexts?.GetValueOrDefault(c.Dto.Id)?.Summary : null,
            parts.Contains("plan_notes") ? chapterTexts?.GetValueOrDefault(c.Dto.Id)?.Notes : null);

        var acts = outline.Acts
            .Where(a => scope == null || scope.Kind != "act" || a.Dto.Id == scope.Id)
            .Select(a => new ActLine(a.Item(target.Id), outline.Chapters
                .Where(c => c.Act == a && (scope?.Kind != "chapter" || c.Dto.Id == scope.Id))
                .Select(Chapter).ToList()))
            .Where(a => scope?.Kind != "chapter" || a.Chapters.Count > 0)
            .ToList();
        var archived = parts.Contains("archived") ? outline.ArchivedChapters.Select(Chapter).ToList() : null;
        IReadOnlyList<KbCategoryLine>? index = null;
        if (parts.Contains("kb_index"))
        {
            var bookKb = await kb.GetBookKbAsync(target.Id, cancellationToken);
            index = bookKb.Categories.Select(c => new KbCategoryLine(
                c.Id, c.Name, c.FromSeries ? "series" : "book", c.SeriesName, bookKb.Entries.Count(e => e.CategoryId == c.Id))).ToList();
        }
        return new GetOutlineResult(acts, archived, index) { Item = scope ?? target.Item };
    }

    // Why a scene waits on the author, or null: the Needs research label, a beat with nothing written under it, or a
    // scene with neither summary nor prose.
    private static string? WaitingReason(OutlineSceneDto scene, bool hasBeat)
    {
        var reasons = new List<string>();
        if (scene.Labels.Any(l => string.Equals(l.Name, NeedsResearch, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add(NeedsResearch);
        }
        if (scene.Words == 0 && hasBeat)
        {
            reasons.Add("beat with no prose");
        }
        else if (scene.Words == 0 && string.IsNullOrWhiteSpace(scene.Summary))
        {
            reasons.Add("No summary and no prose");
        }
        return reasons.Count == 0 ? null : string.Join(" · ", reasons);
    }
}
