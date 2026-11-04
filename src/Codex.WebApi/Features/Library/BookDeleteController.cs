using Codex.Application.Features.Library;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Library;

// GET    /api/books/{id}/delete-impact -> BookDeleteImpactDto (live chapters, scenes, words, sources). Unknown -> 404.
// DELETE /api/books/{id} -> 204. Deletes the book and everything in it for good. A book that is not archived -> 409; unknown -> 404.
[Route("api/books")]
public sealed class BookDeleteController(BookDeleteCommands books) : ApiControllerBase
{
    [HttpGet("{bookId:guid}/delete-impact")]
    [ProducesResponseType<BookDeleteImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookDeleteImpactDto> Impact(Guid bookId, CancellationToken ct)
        => books.ImpactAsync(bookId, ct);

    [HttpDelete("{bookId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<NoContentResult> Delete(Guid bookId, CancellationToken ct)
        => NoContentAfter(books.DeleteAsync(bookId, ct));
}
