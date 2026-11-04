using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.References;

// GET /api/books/{id}/markers            -> MarkerDto[] in reading order (pending-draft markers included, with
//                                           generationId). Unknown book 404 not_found.
// GET /api/books/{id}/references/summary -> ClearanceSummaryDto. Unknown or archived book 404 not_found.
[Route("api/books/{id:guid}")]
public sealed class ReferencesController(IReferenceQueries queries) : ApiControllerBase
{
    [HttpGet("markers")]
    [ProducesResponseType<IReadOnlyList<MarkerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<MarkerDto>> Markers(Guid id, CancellationToken ct) => queries.GetMarkersAsync(id, ct);

    [HttpGet("references/summary")]
    [ProducesResponseType<ClearanceSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ClearanceSummaryDto> Summary(Guid id, CancellationToken ct) => queries.GetSummaryAsync(id, ct);
}
