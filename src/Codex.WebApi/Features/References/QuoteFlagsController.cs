using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.References;

// GET  /api/books/{id}/quote-flags             -> the open flags in reading order (QuoteFlagDto[]). 404 for an unknown book.
// POST /api/quote-flags/{id}/not-a-quote       -> the book's remaining open flags. 404 unknown flag; 409 already resolved.
// POST /api/quote-flags/{id}/other-source      {note} -> the remaining open flags. 400 for a blank note; 404; 409.
// A resolved flag keeps its row, so the same quote is never raised again for the book.
[Route("api")]
public sealed class QuoteFlagsController(IQuoteFlags flags) : ApiControllerBase
{
    [HttpGet("books/{bookId:guid}/quote-flags")]
    [ProducesResponseType<IReadOnlyList<QuoteFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<QuoteFlagDto>> List(Guid bookId, CancellationToken ct) => flags.ListAsync(bookId, ct);

    [HttpPost("quote-flags/{id:guid}/not-a-quote")]
    [ProducesResponseType<IReadOnlyList<QuoteFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<QuoteFlagDto>> NotAQuote(Guid id, CancellationToken ct) => flags.NotAQuoteAsync(id, ct);

    [HttpPost("quote-flags/{id:guid}/other-source")]
    [ProducesResponseType<IReadOnlyList<QuoteFlagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<QuoteFlagDto>> OtherSource(Guid id, OtherSourceRequest request, CancellationToken ct) => flags.OtherSourceAsync(id, request.Note, ct);
}
