using System.ComponentModel;
using Agoda.IoC.Core;
using Codex.Application.Features.Ai;
using Codex.Application.Features.Jobs;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Codex.Contracts.Prompts;
using Dapper;
using ModelContextProtocol.Server;
using Npgsql;

namespace Codex.Application.Mcp;

// D. Drafts (#20, M5, M9, M10, M12): draft_prose (Codex generates, as a job), submit_draft (the agent wrote it),
// resolve_draft (apply or discard, with undo) and get_job. A draft never goes into a scene except through
// resolve_draft(apply) (M12); the text is read with get_draft.
[McpServerToolType]
public sealed class DraftTools(
    McpScenes scenes, McpQueries queries, DraftQueries drafts, DraftCommands commands, McpDraftRunner runner, McpGenerationBudget budget,
    McpChangeSets changeSets, IPromptCatalogue catalogue, McpClientDrafts clientDrafts, IJobQueries exportJobs, IBookAccess access,
    McpDraftIntegrations integrations)
{
    private const int MaxSubmittedChars = 200_000;

    public sealed record DraftStarted(Guid Generation, Guid JobId, string State, int Attempt, string Model, string? Template, int? TargetWords,
        int SceneVersion, int GenerationsLeft) : McpWriteResult;

    public sealed record DraftSubmitted(Guid Generation, string Status, int Words, int SceneVersion) : McpWriteResult;

    public sealed record DraftResolved(string Action, int? Words) : McpWriteResult;

    public sealed record JobView(Guid JobId, string Kind, string State, int? Percent, string? Message, string? ResultUrl, Guid? Generation,
        string? DraftStatus, int? Words, McpItem? Scene) : McpResult;

    [McpServerTool(Name = "draft_prose", Title = "Write a draft of this for me", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Has Codex write a draft with the author's connected AI account (their default integration in Settings → AI, or the one named in " +
                 "`integration`) and the Write screen's prompt (the book's style guide, the beat's context, the prose " +
                 "before it). Give `beat` (prose for that beat) or `scene` (continue at the end of the scene). Returns the generation id and a " +
                 "job_id at once; the draft is written in the background. Read it with get_draft or get_job once the state is done, then " +
                 "resolve_draft. Never applies. Each token can start 20 of these per sliding hour by default (CODEX_MCP_MAX_GENERATIONS sets the limit); past it the call fails rate_limited. To supply the text yourself, use submit_draft.")]
    public async Task<DraftStarted> DraftProseAsync(
        McpServer server,
        [Description("Write the prose for this beat (its id).")] Guid? beat = null,
        [Description("Or continue this scene at its end: an id or \"5.2\" with `book`.")] string? scene = null,
        [Description("A new attempt of this earlier draft; the earlier one is discarded once this one starts.")] Guid? retry_of = null,
        [Description("A prompt template, by id or name (see the Write screen's picker).")] string? template = null,
        [Description("A model id; default the integration's default model (an anthropic integration falls back to Sonnet 5.5; other kinds need one).")] string? model = null,
        [Description("About how long the draft should be; default the beat's own target, else 400.")] int? target_word_count = null,
        [Description("The book, when `scene` is a display number.")] string? book = null,
        [Description("The AI integration to write with, by id or label; default the author's default one.")] string? integration = null,
        CancellationToken cancellationToken = default)
    {
        var (target, beatId, sceneId) = await TargetAsync(server, beat, scene, book, cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var chosen = await integrations.ChooseAsync(integration, cancellationToken);
        if (retry_of is { } earlier)
        {
            // The earlier draft is discarded once this one starts, so it must be in a book the caller can write in.
            await access.RequireAsync(BookItem.Generation, earlier, BookRole.Write, cancellationToken, $"No draft {earlier}.");
        }
        var prompts = catalogue.Get();
        var request = new GenerateDraftRequest(
            template == null ? null : prompts.Templates.FirstOrDefault(t => string.Equals(t.Id, template, StringComparison.OrdinalIgnoreCase)
                                                                        || string.Equals(t.Name, template, StringComparison.OrdinalIgnoreCase))?.Id
                                      ?? throw McpToolException.Invalid("template", $"No template \"{template}\"."),
            model ?? chosen?.DefaultModel ?? CatalogueModel(chosen, prompts),
            target_word_count, retry_of, integration == null ? null : chosen?.Id);

        var caller = McpCaller.From(server);
        var key = budget.Reserve(caller);
        DraftStreamStarted started;
        try
        {
            started = await runner.StartAsync(beatId, sceneId, request, caller, cancellationToken);
        }
        catch
        {
            budget.Release(key);
            throw;
        }

        var version = (await queries.SceneProseAsync(target.Scene.Dto.Id, cancellationToken)).Version;
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "draft_prose", $"Started a draft for {target.Item.Label}", caller,
            [new McpUndoStep("discard_draft", started.GenerationId)], cancellationToken);
        return new DraftStarted(started.GenerationId, started.GenerationId, "running", started.Attempt, started.Model, started.Template,
            started.TargetWords, version, budget.Remaining(key))
        {
            Item = DraftItem(started.GenerationId, target), Version = version, UndoId = undoId,
            SideEffects = [$"Codex is writing a draft for {target.Item.Label}; it waits for the author in Write, and nothing is applied."],
            Affected = [new McpAffected("draft", started.GenerationId, "status", null, "streaming")],
        };
    }

    // The catalogue's default is an Anthropic id: right for an anthropic integration (or none at all), wrong for any other
    // provider, which gets an empty model so the draft fails with "Choose a model for this draft." instead of a provider call.
    private static string CatalogueModel(McpDraftIntegrations.Choice? chosen, PromptsDto prompts) =>
        chosen is null || chosen.Kind == AiIntegrationKinds.Anthropic
            ? prompts.Models.FirstOrDefault(m => m.Default)?.Id ?? prompts.Models[0].Id
            : string.Empty;

    [McpServerTool(Name = "submit_draft", Title = "Offer a draft I wrote", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Stores prose you wrote as a pending draft for a beat (`beat`) or the end of a scene (`scene`), in the same Apply · Retry · Discard " +
                 "panel as Codex's own. It names no model (it is marked as written by this client) and does not use the generation budget. " +
                 "The text may not contain ::beat or ::kb-addition lines, ::figure lines or ref: links. Nothing is applied: use resolve_draft.")]
    public async Task<DraftSubmitted> SubmitDraftAsync(
        McpServer server,
        [Description("The prose, as Markdown in the stored scene format: blocks separated by one blank line, with headings, lists, quotes and tables allowed. Read codex://scene/{id} to see a scene as stored.")] string text,
        [Description("The beat this is the prose for (its id).")] Guid? beat = null,
        [Description("Or the scene it continues at the end: an id or \"5.2\" with `book`.")] string? scene = null,
        [Description("The book, when `scene` is a display number.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw McpToolException.Invalid("text", "A draft needs text.");
        }
        if (text.Length > MaxSubmittedChars)
        {
            throw McpToolException.Invalid("text", $"A draft is at most {MaxSubmittedChars} characters.");
        }
        McpProseRules.RefuseInvented("text", "", text);
        var (target, beatId, sceneId) = await TargetAsync(server, beat, scene, book, cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var caller = McpCaller.From(server);
        var words = ManuscriptText.CountWords(text);
        var id = await clientDrafts.InsertAsync(target.Outline.Book.Id, target.Scene.Dto.Id, beatId, text.Trim(), words, caller, cancellationToken);
        var version = (await queries.SceneProseAsync(sceneId ?? target.Scene.Dto.Id, cancellationToken)).Version;
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "submit_draft", $"Offered a draft for {target.Item.Label}", caller,
            [new McpUndoStep("discard_draft", id)], cancellationToken);
        return new DraftSubmitted(id, "pending", words, version)
        {
            Item = DraftItem(id, target), Version = version, UndoId = undoId,
            SideEffects = [$"A {words}-word draft waits for the author in Write on {target.Item.Label}; nothing is applied."],
            Affected = [new McpAffected("draft", id, "status", null, "pending")],
        };
    }

    [McpServerTool(Name = "resolve_draft", Title = "Keep it or bin it", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Applies a pending draft to its scene (a beat's draft replaces the prose under the beat; a continuation goes at the end) or discards it. " +
                 "`apply` needs expected_version, the scene's version from read_manuscript or get_draft, and a stale one fails with conflict. " +
                 "Both can be undone. For a new attempt, call draft_prose with retry_of.")]
    public async Task<DraftResolved> ResolveDraftAsync(
        McpServer server,
        [Description("The draft (generation) id.")] Guid generation,
        [Description("`apply` or `discard`.")] string action,
        [Description("For apply: the scene's version.")] int? expected_version = null,
        CancellationToken cancellationToken = default)
    {
        var apply = action switch
        {
            "apply" => true,
            "discard" => false,
            _ => throw McpToolException.Invalid("action", "action is \"apply\" or \"discard\"."),
        };
        var dto = await drafts.GetAsync(generation, cancellationToken);
        var target = await scenes.SceneAsync(server, null, dto.SceneId.ToString(), cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var caller = McpCaller.From(server);
        if (dto.Status == "streaming")
        {
            throw new McpToolException(McpErrorCodes.Conflict, "That draft is still being written. Check get_job, then resolve it.") { Candidates = [DraftItem(generation, target)] };
        }
        if (dto.Status != "pending")
        {
            throw new McpToolException(McpErrorCodes.Conflict, $"That draft is {dto.Status}; only a pending draft can be resolved.") { Candidates = [DraftItem(generation, target)] };
        }

        if (!apply)
        {
            await commands.DiscardAsync(generation, cancellationToken);
            var discardUndo = await changeSets.RecordAsync(target.Outline.Book.Id, "resolve_draft", $"Discarded a draft of {target.Item.Label}", caller,
                [new McpUndoStep("restore_draft", generation)], cancellationToken);
            return new DraftResolved("discard", null)
            {
                Item = DraftItem(generation, target), UndoId = discardUndo,
                SideEffects = [$"The draft of {target.Item.Label} is discarded."],
                Affected = [new McpAffected("draft", generation, "status", "pending", "discarded")],
            };
        }

        if (expected_version is not { } expected)
        {
            throw McpToolException.Invalid("expected_version", "Applying a draft needs expected_version: the scene's version.");
        }
        var current = await queries.SceneProseAsync(dto.SceneId, cancellationToken);
        if (current.Version != expected)
        {
            throw ProseTools.Conflict(target, expected, current.Version);
        }
        DraftApplyResult applied;
        try
        {
            applied = await commands.ApplyAsync(generation, new DraftVersionRequest(expected), cancellationToken);
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
        {
            throw ProseTools.Conflict(target, expected, (await queries.SceneProseAsync(dto.SceneId, cancellationToken)).Version);
        }
        var after = await scenes.ReloadAsync(server, target, cancellationToken);
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "resolve_draft", $"Applied a draft to {target.Item.Label}", caller,
            [new McpUndoStep("undo_apply", generation, Version: applied.Version)], cancellationToken);
        var words = ManuscriptText.CountWords(applied.Body);
        return new DraftResolved("apply", words)
        {
            Item = after.Item, Version = applied.Version, UndoId = undoId,
            SideEffects = [$"{after.Item.Label} now has {words} words; its previous text is kept in History."],
            Affected = [new McpAffected("draft", generation, "status", "pending", "applied")],
        };
    }

    [McpServerTool(Name = "get_job", Title = "Is it done yet?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The state of a job by its id: a draft_prose job (the job_id it returned; running, done or failed, with the draft's status and words " +
                 "once done; read the text with get_draft), or an export job. Poll until the state is done or failed.")]
    public async Task<JobView> GetJobAsync(
        McpServer server,
        [Description("The job id.")] Guid job_id,
        CancellationToken cancellationToken = default)
    {
        if (await queries.BookOfAsync(null, job_id, cancellationToken) is not null)
        {
            var dto = await drafts.GetAsync(job_id, cancellationToken);
            var target = await scenes.SceneAsync(server, null, dto.SceneId.ToString(), cancellationToken);
            var state = dto.Status switch { "streaming" => "running", "failed" => "failed", _ => "done" };
            return new JobView(job_id, "draft", state, state == "running" ? null : 100,
                state == "failed" ? "Codex could not write the draft." : null, null, job_id, dto.Status, dto.Words, target.Item)
            {
                Item = DraftItem(job_id, target),
            };
        }

        await access.RequireAsync(BookItem.ExportJob, job_id, BookRole.View, cancellationToken, $"Job {job_id} was not found.");
        var export = await exportJobs.GetJobAsync(job_id, cancellationToken);
        return new JobView(export.Id, export.Kind, export.State, export.Percent, export.Message, export.ResultUrl, null, null, null, null);
    }

    private async Task<(McpScenes.Target Target, Guid? BeatId, Guid? SceneId)> TargetAsync(
        McpServer server, Guid? beat, string? scene, string? book, CancellationToken cancellationToken)
    {
        if ((beat == null) == (scene == null))
        {
            throw McpToolException.Invalid("beat", "Give exactly one of `beat` (prose for that beat) or `scene` (continue the scene).");
        }
        if (beat is { } beatId)
        {
            var sceneId = await queries.SceneOfBeatAsync(beatId, cancellationToken) ?? throw McpToolException.NotFound($"No live beat {beatId}.");
            return (await scenes.SceneAsync(server, null, sceneId.ToString(), cancellationToken), beatId, null);
        }
        var target = await scenes.SceneAsync(server, book, scene!, cancellationToken);
        return (target, null, target.Scene.Dto.Id);
    }

    private static McpItem DraftItem(Guid id, McpScenes.Target target) => new(id, "draft", $"{target.Item.Label} · draft", target.Item.OpenUrl);
}
