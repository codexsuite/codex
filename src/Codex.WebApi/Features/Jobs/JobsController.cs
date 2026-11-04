using Codex.Application.Features.Jobs;
using Codex.Contracts.Errors;
using Codex.Contracts.Jobs;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Jobs;

// GET /api/jobs/{id} -> JobDto. Unknown id 404 not_found.
[Route("api/jobs")]
public sealed class JobsController(IJobQueries queries) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<JobDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<JobDto> Get(Guid id, CancellationToken ct) => queries.GetJobAsync(id, ct);
}
