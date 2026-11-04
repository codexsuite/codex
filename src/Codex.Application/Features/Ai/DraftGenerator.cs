using System.Runtime.CompilerServices;
using System.Text;
using Agoda.IoC.Core;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.Contracts.Prompts;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai;

// Runs a generation: the ai_generation row is created first (status streaming), the generator's chunks stream out as
// DraftStreamEvents, and the row ends pending (completed, or stopped with text), discarded (stopped with none) or failed.
[RegisterSingleton]
public sealed class DraftGenerator(
    NpgsqlDataSource dataSource, IProseGenerator generator, IPromptTemplates templates, ICurrentUser currentUser, IBookAccess access,
    IAiIntegrationResolver integrations)
{
    private const int DefaultTargetWords = 400;
    private const string Pending = "pending";
    private const int MaxModelLength = 200;

    private sealed record Target(Guid BookId, Guid SceneId, Guid? BeatId, string? BeatBody, int? BeatWords, string Body, string? StyleGuide);

    private enum Outcome { Completed, Stopped, Failed }

    public Task<IAsyncEnumerable<DraftStreamEvent>> GenerateForBeatAsync(Guid beatId, GenerateDraftRequest request, CancellationToken cancellationToken)
        => StartAsync(beatId, null, request, cancellationToken);

    public Task<IAsyncEnumerable<DraftStreamEvent>> ContinueSceneAsync(Guid sceneId, GenerateDraftRequest request, CancellationToken cancellationToken)
        => StartAsync(null, sceneId, request, cancellationToken);

    // Everything that can be refused (unknown beat, bad model, a generator that fails before its first chunk) throws
    // here, before the HTTP response begins. The returned stream starts with the `started` line.
    private async Task<IAsyncEnumerable<DraftStreamEvent>> StartAsync(Guid? beatId, Guid? sceneId, GenerateDraftRequest request, CancellationToken cancellationToken)
    {
        // Access first (#29, #80): a beat or scene the user cannot open is not found, whatever the request says, and
        // drafting needs write. The integration and template below are always the drafting user's own (#36), never the
        // book owner's.
        if (beatId is { } beat)
        {
            await access.RequireAsync(BookItem.Beat, beat, BookRole.Write, cancellationToken, "Beat not found.");
        }
        else
        {
            await access.RequireAsync(BookItem.Scene, sceneId!.Value, BookRole.Write, cancellationToken, "Scene not found.");
        }

        // The author's integration: the one the request names (another user's is not found), else their default.
        var integration = await integrations.ResolveAsync(request.IntegrationId, cancellationToken);
        var model = ChooseModel(request.Model, integration);

        // A built-in template or one of the author's own (Settings -> AI); another user's is an unknown template.
        var template = request.TemplateId == null ? null : await templates.ResolveAsync(request.TemplateId, cancellationToken);
        var templateName = template?.Name;

        if (request.TargetWords is <= 0)
        {
            throw ApiException.Validation("targetWords must be positive.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var target = await LoadTargetAsync(connection, beatId, sceneId, cancellationToken);
        var targetWords = request.TargetWords ?? target.BeatWords ?? DefaultTargetWords;
        var attempt = await NextAttemptAsync(connection, request.RetryOfId, target, cancellationToken);

        var entries = (await connection.QueryAsync<DraftPrompt.Entry>(new CommandDefinition(
            """
            SELECT e.name AS Name, e.summary AS Summary
            FROM kb_entry e
            WHERE e.archived_at IS NULL
              AND (e.id IN (SELECT kb_entry_id FROM beat_kb WHERE beat_id = @beatId AND archived_at IS NULL)
                   OR (e.always_in_context AND e.book_id IN (SELECT @bookId UNION SELECT series_id FROM book WHERE id = @bookId)))
            ORDER BY e.name, e.id
            """,
            new { beatId, bookId = target.BookId }, cancellationToken: cancellationToken))).ToArray();
        var styleGuide = target.StyleGuide;
        var prior = PriorProse(target);
        // A user's template is the whole prompt, its variables filled in; a built-in one is an instruction before the standard prompt.
        var proseRequest = template is { BuiltIn: false }
            ? new ProseRequest(model, "", DraftPrompt.FromTemplate(template.Text, target.BeatBody, targetWords, entries, prior, styleGuide), integration?.Id)
            : new ProseRequest(model, DraftPrompt.Instruction(template?.Text, styleGuide), DraftPrompt.Prompt(target.BeatBody, targetWords, entries, prior), integration?.Id);

        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO ai_generation (book_id, scene_id, beat_id, kind, model, request, status, retry_of_id, created_by, integration_id)
            VALUES (@bookId, @sceneId, @beatId, @kind, @model, @request::jsonb, 'streaming', @retryOfId, @userId, @integrationId)
            RETURNING id
            """,
            new
            {
                bookId = target.BookId,
                sceneId = target.SceneId,
                beatId = target.BeatId,
                kind = target.BeatId == null ? "continue" : "beat_prose",
                model,
                request = System.Text.Json.JsonSerializer.Serialize(new
                {
                    attempt,
                    templateId = request.TemplateId,
                    template = templateName,
                    beat = target.BeatBody,
                    targetWords,
                    context = entries.Select(e => e.Name).ToArray(),
                    // Exactly what the provider is sent, for "Show what was sent" (GET /api/generations/{id}/sent).
                    integration = integration?.Label,
                    instruction = proseRequest.Instruction,
                    prompt = proseRequest.Prompt,
                }),
                retryOfId = request.RetryOfId,
                userId = currentUser.Id,
                integrationId = integration?.Id,
            }, cancellationToken: cancellationToken));

        IAsyncEnumerator<ProseChunk> enumerator;
        ProseChunk first;
        try
        {
            enumerator = generator.GenerateAsync(proseRequest, cancellationToken).GetAsyncEnumerator(cancellationToken);
            first = await enumerator.MoveNextAsync() ? enumerator.Current : new ProseChunk(string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseAsync(id, Outcome.Stopped, "", null, CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            await CloseAsync(id, Outcome.Failed, "", null, CancellationToken.None);
            throw;
        }

        // A retry replaces the draft it retries: the old one stops being pending once the new one is under way.
        if (request.RetryOfId is { } retried)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ai_generation SET status = 'discarded', resolved_at = now() WHERE id = @retried AND status = 'pending'",
                new { retried }, cancellationToken: cancellationToken));
        }

        var started = new DraftStreamStarted(id, attempt, model, request.TemplateId, templateName, targetWords);
        return Stream(started, target.BookId, currentUser.Id, enumerator, first, cancellationToken);
    }

    private async IAsyncEnumerable<DraftStreamEvent> Stream(
        DraftStreamStarted started, Guid bookId, Guid userId, IAsyncEnumerator<ProseChunk> enumerator, ProseChunk first,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var outcome = Outcome.Stopped;
        var closed = false;
        ProseResult? result = null;
        Exception? failure = null;
        try
        {
            yield return new DraftStreamEvent(Started: started);
            var chunk = first;
            while (true)
            {
                if (chunk.Final != null)
                {
                    result = chunk.Final;
                }
                else if (chunk.Text.Length > 0)
                {
                    text.Append(chunk.Text);
                    yield return new DraftStreamEvent(Text: chunk.Text);
                }

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        outcome = Outcome.Completed;
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    outcome = Outcome.Failed;
                    failure = e;
                    break;
                }

                chunk = enumerator.Current;
            }

            if (outcome == Outcome.Completed && !await CanStillWriteAsync(bookId))
            {
                outcome = Outcome.Failed;
                failure = LostAccess();
            }
            await CloseAsync(started.GenerationId, outcome, text.ToString(), result, CancellationToken.None);
            closed = true;
            if (outcome == Outcome.Stopped)
            {
                await FailIfAccessLostAsync(started.GenerationId, bookId, userId);
            }
            if (outcome == Outcome.Completed)
            {
                yield return new DraftStreamEvent(Final: new DraftStreamFinal(
                    started.GenerationId, result?.Model ?? started.Model, result?.InputTokens ?? 0, result?.OutputTokens ?? 0, WordCount(text.ToString())));
            }
            else if (failure != null)
            {
                yield return new DraftStreamEvent(Error: failure is ApiException api
                    ? new ApiError(api.Message, api.Code)
                    : new ApiError("The draft could not be generated.", ApiErrorCodes.Internal));
            }
        }
        finally
        {
            if (!closed)
            {
                // The consumer went away mid-stream: that is a stop, and the text received so far is kept (unless the
                // user lost write access meanwhile).
                await CloseAsync(started.GenerationId, Outcome.Stopped, text.ToString(), null, CancellationToken.None);
                await FailIfAccessLostAsync(started.GenerationId, bookId, userId);
            }

            await enumerator.DisposeAsync();
        }
    }

    // A draft can outlive the drafter's write access (an MCP draft runs in the background, a long stream keeps going):
    // the role is read again when the draft ends (before closing a completed draft, so the stream can say why; after
    // closing a stopped one, whose consumer may be gone), and a draft whose author can no longer write fails instead of
    // waiting as pending in a book they cannot change (#80).
    private async Task<bool> CanStillWriteAsync(Guid bookId)
        => await access.RoleAsync(bookId, CancellationToken.None) is >= BookRole.Write;

    private async Task FailIfAccessLostAsync(Guid id, Guid bookId, Guid userId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ai_generation SET status = 'failed', resolved_at = now()
            WHERE id = @id AND status = 'pending'
              AND NOT EXISTS (SELECT 1 FROM book_access a WHERE a.book_id = @bookId AND a.user_id = @userId AND a.role_rank >= 3)
            """,
            new { id, bookId, userId }, cancellationToken: CancellationToken.None));
    }

    private static ApiException LostAccess()
        => ApiException.Forbidden("You can no longer edit this book, so the draft was not kept.");

    // Models are free text per integration (the catalogue only suggests): the request's, else the integration's default.
    private static string ChooseModel(string? requested, ResolvedAiIntegration? integration)
    {
        var model = requested?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            model = integration?.DefaultModel?.Trim();
        }

        if (string.IsNullOrEmpty(model))
        {
            throw ApiException.Validation("Choose a model for this draft.");
        }

        return model.Length > MaxModelLength || model.Any(char.IsControl)
            ? throw ApiException.Validation("That model name is not valid.")
            : model;
    }

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private async Task CloseAsync(Guid id, Outcome outcome, string text, ProseResult? result, CancellationToken cancellationToken)
    {
        var status = outcome switch
        {
            Outcome.Completed => Pending,
            Outcome.Stopped when text.Length > 0 => Pending,
            Outcome.Stopped => "discarded",
            _ => "failed",
        };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ai_generation
            SET output = @text, status = @status, input_tokens = @inputTokens, output_tokens = @outputTokens,
                resolved_at = CASE WHEN @status = 'pending' THEN NULL ELSE now() END
            WHERE id = @id AND status = 'streaming'
            """,
            new { id, text, status, inputTokens = result?.InputTokens, outputTokens = result?.OutputTokens }, cancellationToken: cancellationToken));
    }

    private static string PriorProse(Target target)
    {
        if (target.BeatId is not { } beatId)
        {
            return target.Body;
        }

        var beat = ProseDocument.Parse(target.Body).Blocks.FirstOrDefault(b => b.Directive == "beat" && b.Id == beatId);
        return beat == null ? target.Body : target.Body[..beat.Start];
    }

    private static async Task<Target> LoadTargetAsync(NpgsqlConnection connection, Guid? beatId, Guid? sceneId, CancellationToken cancellationToken)
    {
        const string StyleGuide =
            """
            (SELECT string_agg(bk.style_guide, E'\n\n' ORDER BY bk.kind DESC) FROM book bk
             WHERE bk.style_guide IS NOT NULL AND (bk.id = s.book_id OR bk.id = (SELECT series_id FROM book WHERE id = s.book_id)))
            """;
        var row = beatId != null
            ? await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
                $"""
                SELECT s.book_id AS BookId, s.id AS SceneId, b.id AS BeatId, b.body AS BeatBody, b.target_word_count AS BeatWords,
                       coalesce(p.body, '') AS Body, {StyleGuide} AS StyleGuide
                FROM beat b JOIN scene s ON s.id = b.scene_id LEFT JOIN prose p ON p.scene_id = s.id
                WHERE b.id = @beatId AND b.archived_at IS NULL AND s.archived_at IS NULL
                """,
                new { beatId }, cancellationToken: cancellationToken))
            : await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
                $"""
                SELECT s.book_id AS BookId, s.id AS SceneId, NULL::uuid AS BeatId, NULL::text AS BeatBody, NULL::int AS BeatWords,
                       coalesce(p.body, '') AS Body, {StyleGuide} AS StyleGuide
                FROM scene s LEFT JOIN prose p ON p.scene_id = s.id
                WHERE s.id = @sceneId AND s.archived_at IS NULL
                """,
                new { sceneId }, cancellationToken: cancellationToken));
        return row ?? throw ApiException.NotFound(beatId != null ? "Beat not found." : "Scene not found.");
    }

    private static async Task<int> NextAttemptAsync(NpgsqlConnection connection, Guid? retryOfId, Target target, CancellationToken cancellationToken)
    {
        if (retryOfId is not { } retried)
        {
            return 1;
        }

        var previous = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            SELECT coalesce((request->>'attempt')::int, 1) FROM ai_generation
            WHERE id = @retried AND scene_id = @sceneId AND beat_id IS NOT DISTINCT FROM @beatId
            """,
            new { retried, sceneId = target.SceneId, beatId = target.BeatId }, cancellationToken: cancellationToken));
        return (previous ?? throw ApiException.Validation("retryOfId is not a generation of this beat.")) + 1;
    }
}
