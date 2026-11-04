using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Application.Features.Ai;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Ai;

// GET  /api/books/{id}/drafts          -> BookDraftsDto (pending drafts, one request per screen)
// POST /api/beats/{id}/generate        GenerateDraftRequest -> application/x-ndjson of DraftStreamEvent (a draft for the beat)
// POST /api/scenes/{id}/continue       GenerateDraftRequest -> the same stream, a continuation from the scene's end (no beat)
// GET  /api/generations/{id}/sent      -> DraftSentDto (what was sent: template, model, context names, tokens, prompt)
// POST /api/generations/{id}/apply     DraftVersionRequest -> DraftApplyResult; inserts after the beat (or at the scene's end), cuts an ai_apply revision
// POST /api/generations/{id}/undo-apply DraftVersionRequest -> DraftApplyResult; puts the pre-apply body back, the draft is pending again
// POST /api/generations/{id}/discard   -> DraftDto (discarded);  POST /api/generations/{id}/undo-discard -> DraftDto (pending)
// Stale version, a draft that is not pending (applied twice) 409; unknown beat, scene or generation 404; bad model or
// template 400; no provider configured 503 not_configured (before the stream starts).
[Route("api")]
public sealed class DraftsController(DraftQueries queries, DraftGenerator generator, DraftCommands commands) : ApiControllerBase
{
    private static readonly JsonSerializerOptions EventOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [HttpGet("books/{bookId:guid}/drafts")]
    [ProducesResponseType<BookDraftsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookDraftsDto> List(Guid bookId, CancellationToken ct) => queries.ListAsync(bookId, ct);

    [HttpGet("generations/{generationId:guid}/sent")]
    [ProducesResponseType<DraftSentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<DraftSentDto> Sent(Guid generationId, CancellationToken ct) => queries.SentAsync(generationId, ct);

    [HttpPost("beats/{beatId:guid}/generate")]
    [ProducesResponseType<DraftStreamEvent>(StatusCodes.Status200OK, "application/x-ndjson")]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EmptyResult> Generate(Guid beatId, GenerateDraftRequest request, CancellationToken ct)
        => await WriteStreamAsync(await generator.GenerateForBeatAsync(beatId, request, ct), ct);

    [HttpPost("scenes/{sceneId:guid}/continue")]
    [ProducesResponseType<DraftStreamEvent>(StatusCodes.Status200OK, "application/x-ndjson")]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EmptyResult> Continue(Guid sceneId, GenerateDraftRequest request, CancellationToken ct)
        => await WriteStreamAsync(await generator.ContinueSceneAsync(sceneId, request, ct), ct);

    [HttpPost("generations/{generationId:guid}/apply")]
    [ProducesResponseType<DraftApplyResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<DraftApplyResult> Apply(Guid generationId, DraftVersionRequest request, CancellationToken ct)
        => commands.ApplyAsync(generationId, request, ct);

    [HttpPost("generations/{generationId:guid}/undo-apply")]
    [ProducesResponseType<DraftApplyResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<DraftApplyResult> UndoApply(Guid generationId, DraftVersionRequest request, CancellationToken ct)
        => commands.UndoApplyAsync(generationId, request, ct);

    [HttpPost("generations/{generationId:guid}/discard")]
    [ProducesResponseType<DraftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<DraftDto> Discard(Guid generationId, CancellationToken ct) => commands.DiscardAsync(generationId, ct);

    [HttpPost("generations/{generationId:guid}/undo-discard")]
    [ProducesResponseType<DraftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<DraftDto> UndoDiscard(Guid generationId, CancellationToken ct) => commands.UndoDiscardAsync(generationId, ct);

    // One JSON object per line, flushed as it is produced. A client that goes away ends the stream; the generator
    // service keeps what was received.
    private async Task<EmptyResult> WriteStreamAsync(IAsyncEnumerable<DraftStreamEvent> events, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson";
        Response.Headers.CacheControl = "no-store";
        try
        {
            await foreach (var streamEvent in events.WithCancellation(ct))
            {
                await Response.WriteAsync(JsonSerializer.Serialize(streamEvent, EventOptions) + "\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop: the client closed the request.
        }

        return new EmptyResult();
    }
}
