using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// GET   /api/books/{id}/beats                 -> BookBeatsDto (live beats of live scenes, with their context entry ids)
// POST  /api/scenes/{id}/beats                -> InsertBeatResult; creates the beat row and saves the body holding its ::beat line
// PATCH /api/beats/{id}                       -> BeatDto
// POST  /api/scenes/{id}/split                -> SplitSceneResult; a new scene after this one takes the tail body
// POST  /api/blocks/move                      -> MoveBlockResult; two scenes' bodies saved together, beats follow their lines
// Stale version 409; unknown scene or beat 404; an embed the schema rejects 400.
[Route("api")]
public sealed class BlocksController(BlockCommands commands) : ApiControllerBase
{
    [HttpGet("books/{bookId:guid}/beats")]
    [ProducesResponseType<BookBeatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookBeatsDto> List(Guid bookId, CancellationToken ct) => commands.ListAsync(bookId, ct);

    [HttpPost("scenes/{sceneId:guid}/beats")]
    [ProducesResponseType<InsertBeatResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<InsertBeatResult> InsertBeat(Guid sceneId, InsertBeatRequest request, CancellationToken ct)
        => commands.InsertBeatAsync(sceneId, request, ct);

    [HttpPatch("beats/{beatId:guid}")]
    [ProducesResponseType<BeatDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BeatDto> PatchBeat(Guid beatId, PatchBeatRequest request, CancellationToken ct)
        => commands.PatchBeatAsync(beatId, request, ct);

    [HttpPost("scenes/{sceneId:guid}/split")]
    [ProducesResponseType<SplitSceneResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SplitSceneResult> Split(Guid sceneId, SplitSceneRequest request, CancellationToken ct)
        => commands.SplitAsync(sceneId, request, ct);

    [HttpPost("blocks/move")]
    [ProducesResponseType<MoveBlockResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<MoveBlockResult> Move(MoveBlockRequest request, CancellationToken ct) => commands.MoveAsync(request, ct);
}
