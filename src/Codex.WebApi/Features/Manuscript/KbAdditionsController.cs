using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// GET   /api/books/{id}/kb-additions      -> BookKbAdditionsDto (live additions of live scenes, in creation order)
// POST  /api/scenes/{id}/kb-additions     -> InsertKbAdditionResult; creates the row and saves the body holding its ::kb-addition line
// PATCH /api/kb-additions/{id}            -> KbAdditionDto (text and/or entry)
// Stale version 409; unknown scene or addition 404; an entry outside the book's scope 400.
[Route("api")]
public sealed class KbAdditionsController(KbAdditionCommands commands) : ApiControllerBase
{
    [HttpGet("books/{bookId:guid}/kb-additions")]
    [ProducesResponseType<BookKbAdditionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookKbAdditionsDto> List(Guid bookId, CancellationToken ct) => commands.ListAsync(bookId, ct);

    [HttpPost("scenes/{sceneId:guid}/kb-additions")]
    [ProducesResponseType<InsertKbAdditionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<InsertKbAdditionResult> Insert(Guid sceneId, InsertKbAdditionRequest request, CancellationToken ct)
        => commands.InsertAsync(sceneId, request, ct);

    [HttpPatch("kb-additions/{additionId:guid}")]
    [ProducesResponseType<KbAdditionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbAdditionDto> Patch(Guid additionId, PatchKbAdditionRequest request, CancellationToken ct)
        => commands.PatchAsync(additionId, request, ct);
}
