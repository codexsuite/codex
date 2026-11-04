using Codex.Application.Features.Settings;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Settings;

// GET /api/books/{id}/publish -> PublishStateDto: words, chapters, sources at risk and the snapshots. Unknown book -> 404.
// POST /api/books/{id}/publish -> PublishStateDto: sets the status to published and queues a snapshot (a source export job).
//   A book with no live chapter -> 422 blocked ("Nothing to publish. Add a chapter first.").
[Route("api/books")]
public sealed class PublishController(PublishCommands publish) : ApiControllerBase
{
    [HttpGet("{bookId:guid}/publish")]
    [ProducesResponseType<PublishStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PublishStateDto> Get(Guid bookId, CancellationToken ct)
        => publish.GetAsync(bookId, ct);

    [HttpPost("{bookId:guid}/publish")]
    [ProducesResponseType<PublishStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public Task<PublishStateDto> Publish(Guid bookId, CancellationToken ct)
        => publish.PublishAsync(bookId, ct);
}
