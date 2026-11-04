using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// Find and replace over a book's text (J23). Replace and Undo change prose through IProseStore.
// POST /api/books/{bookId:guid}/find           FindRequest -> FindResultDto
// POST /api/books/{bookId:guid}/replace        ReplaceRequest -> ReplaceResultDto
// POST /api/books/{bookId:guid}/replace/undo   ReplaceUndoDto -> { restored }
[Route("api/books/{bookId:guid}")]
public sealed class FindReplaceController(FindReplace service) : ApiControllerBase
{
    [HttpPost("find")]
    [ProducesResponseType<FindResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<FindResultDto> Find(Guid bookId, FindRequest request, CancellationToken ct) => service.FindAsync(bookId, request, ct);

    [HttpPost("replace")]
    [ProducesResponseType<ReplaceResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ReplaceResultDto> Replace(Guid bookId, ReplaceRequest request, CancellationToken ct) => service.ReplaceAsync(bookId, request, ct);

    [HttpPost("replace/undo")]
    [ProducesResponseType<RestoredResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<RestoredResult> Undo(Guid bookId, ReplaceUndoDto request, CancellationToken ct)
        => new(await service.UndoAsync(bookId, request, ct));

    public sealed record RestoredResult(int Restored);
}
