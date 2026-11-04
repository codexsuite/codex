using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// The slash commands of #20 Part 5. Each reads what its journey starts from, in-process, with the same tools the agent
// has, and returns instructions that name the tools to use next: /resume (J1), /draft-scene (J5, J6), /print-readiness
// (J12) and /triage-references (J11). Prompts only read, so they need codex:read (McpScopes.PromptFilter); every
// write they send the agent to make is checked against the token's own scope when the agent calls the tool.
[McpServerPromptType]
public sealed class CodexPrompts(IServiceProvider services)
{
    [McpServerPrompt(Name = "resume", Title = "Pick up where I left off")]
    [Description("A briefing from resume_work, then the first pending AI draft to review.")]
    public async Task<string> ResumeAsync(
        McpServer server,
        [Description("The book; default the most recently edited one.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        BearingsTools.ResumeWorkResult briefing;
        try
        {
            briefing = await ActivatorUtilities.CreateInstance<BearingsTools>(services).ResumeWorkAsync(server, book, cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
        var first = briefing.PendingDrafts.FirstOrDefault();
        var offer = first == null
            ? "There are no pending AI drafts; suggest what to work on next from the scenes waiting on me."
            : $"Then show me the first pending draft, {first.Draft.Item.Label} (draft {first.Draft.Item.Id}, scene version {first.SceneVersion}), " +
              "and ask whether to apply it, retry it or discard it. Nothing is applied until I say so.";
        return $"""
            I'm picking up {briefing.Book.Item.Label} where I left off. This is the resume_work briefing:

            {McpJson.Serialize(briefing)}

            Brief me in a few lines: the scene I was last on, the pending drafts, what is waiting on me and the References risks, each with its open_url.
            {offer}
            """;
    }

    [McpServerPrompt(Name = "draft-scene", Title = "Draft this scene")]
    [Description("Reads a scene, proposes beats for it and drafts them as pending AI drafts for review in Write.")]
    public async Task<string> DraftSceneAsync(
        McpServer server,
        [Description("The scene: an id, or a display number like \"6.2\" with `book`.")] string scene,
        [Description("The book, when the scene is a display number; default the most recently edited one.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        ReadTools.ReadManuscriptResult read;
        McpScenes.Target target;
        try
        {
            target = await services.GetRequiredService<McpScenes>().SceneAsync(server, book, scene, cancellationToken);
            read = await ActivatorUtilities.CreateInstance<ReadTools>(services).ReadManuscriptAsync(
                server, target.Outline.Book.Id.ToString(), target.Scene.Dto.Id.ToString(), cancellationToken: cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
        var label = target.Item.Label;
        return $$"""
            I want to draft {{label}} ({{target.Outline.Book.Title}}). This is the scene as read_manuscript returns it (annotated: beats, Knowledge base additions and pending drafts are shown as text):

            {{McpJson.Serialize(read)}}

            Work like this, in order:
            1. Say in a few lines what the scene is for (its summary and what comes before it) and what is already in it: prose, beats, pending drafts. If a pending draft is waiting, show it first and ask whether to apply it, retry it or discard it before writing more.
            2. Propose the beats: one line each, with a target length in words and the Knowledge base entries each needs as context (find them with search or get_kb_entry). An empty scene needs a summary first; if it has none, ask me what it is for. Wait for me to agree or change them.
            3. When I agree, add each beat with upsert_beat (expected_version is the scene version above; at "end" or after the previous beat; context_add for its entries), then start one draft per beat with draft_prose({ beat }). Use a template only if I name one.
            4. draft_prose runs in the background. Follow it with get_job until it is done, then tell me which drafts are ready in Write, with the open_url. Drafts stay pending: do not call resolve_draft with apply unless I tell you to for a named draft.
            Keep to the book's style guide (get_book_settings) and do not edit the prose yourself.
            """;
    }

    [McpServerPrompt(Name = "print-readiness", Title = "Get this book ready to print")]
    [Description("Works through the copyright clearance blocking print, one source at a time, then the layout preview and the printer's preflight.")]
    public async Task<string> PrintReadinessAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\"; default the most recently edited one.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        ClearanceTools.GetClearanceResult clearance;
        try
        {
            clearance = await ActivatorUtilities.CreateInstance<ClearanceTools>(services).GetClearanceAsync(server, book, cancellationToken: cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
        var blocked = clearance.Summary is { PrintBlocked: true };
        return $$"""
            I'm getting {{clearance.Item?.Label ?? "the book"}} ready to print. This is get_clearance for the whole book (the summary strip, every source with its status and next_action, and the unflagged-quote inbox):

            {{McpJson.Serialize(clearance)}}

            {{(blocked ? "Print is blocked now." : "Nothing blocks print on this summary.")}} Work through it one source at a time, in this order: risks, then needs_check, then the unflagged quotes (flags), then the sources that are not yet cited.
            For each source: call get_clearance({ source }) for its uses, checks and permission, say in two lines why it is where it is and what its next_action means, and propose one fix. The tools for the fixes are update_source (details, licence, a use's type), cite_source, manage_permission (request, chase or record a permission) and record_clearance_decision with not_a_quote or reopen. Make a fix only after I say yes to that source, then call get_clearance again to show what changed. Move to the next source when I say so.
            Sign-off and fair use are my decision as the author, made in References, never by you: when a source is ready (next_action sign_off or decide_fair_use), give me its summary of uses and quoted words with its open_url and go on to the next source. Do not call record_clearance_decision with sign_off or accept_fair_use.
            When the strip reads that no risks block print export, check the layout: preview_layout for the book, and tell me what it shows (the template, the trim, low-resolution images, raw LaTeX blocks). Then run export_for_printer with preflight to list what the printer's checks would flag. Report what is left to fix. Queue the export itself only if I ask.
            """;
    }

    [McpServerPrompt(Name = "triage-references", Title = "Triage unflagged quotes and new uses")]
    [Description("Walks the unflagged-quote inbox and the uses added since sign-off, one at a time.")]
    public async Task<string> TriageReferencesAsync(
        McpServer server,
        [Description("The book: id, title or \"<series>/<number>\"; default the most recently edited one.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        ClearanceTools.GetClearanceResult clearance;
        try
        {
            clearance = await ActivatorUtilities.CreateInstance<ClearanceTools>(services).GetClearanceAsync(server, book, cancellationToken: cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }
        var inbox = clearance.Flags?.Count ?? 0;
        var review = (clearance.Sources ?? []).Where(s => s.NextAction == "review_new_use").ToList();
        return $$"""
            I'm triaging the References of {{clearance.Item?.Label ?? "the book"}}: {{inbox}} possible unflagged quote(s) and {{review.Count}} source(s) with uses added since they were signed off. This is get_clearance for the whole book:

            {{McpJson.Serialize(clearance)}}

            Do the inbox first, one flag at a time. Show me the excerpt, where it is (the scene and its open_url) and any source it matches (source_guess, match_percent). Then ask which it is:
            - not a quote, for example the facilitator's own words: record_clearance_decision({ decision: "not_a_quote", flag }). It is never raised again.
            - a quote or paraphrase of a source: cite_source({ from_flag, source, kind, locator }); say what the source's quoted words and limit become, and whether it is now a risk.
            Then the sources with a review_new_use next_action: get_clearance({ source }) and show only the uses marked new_since_sign_off. If a use is a fair paraphrase, say so and give me the open_url so I can sign the source off again myself; if it needs another type or a locator, fix it with update_source after I say yes.
            Sign-off and fair use are my decision as the author, made in References: never call record_clearance_decision with sign_off or accept_fair_use. Take one item at a time, wait for my answer before writing, and finish with a short list of what is left.
            """;
    }
}
