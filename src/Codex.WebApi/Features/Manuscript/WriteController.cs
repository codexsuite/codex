using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// The Write screen's reads. Per-scene prose reads and writes stay in ManuscriptController.
// GET /api/books/{bookId:guid}/manuscript -> ManuscriptDto
[Route("api/books/{bookId:guid}/manuscript")]
public sealed class WriteController(ManuscriptQuery manuscript) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ManuscriptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ManuscriptDto> Get(Guid bookId, CancellationToken ct) => manuscript.GetAsync(bookId, ct);
}
