using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// A scene's version history (J15). Restore is a POST with no body: it always replaces whatever the scene holds now.
// GET  /api/scenes/{sceneId:guid}/prose/revisions                           -> ProseHistoryDto
// POST /api/scenes/{sceneId:guid}/prose/revisions/{revisionId:guid}/restore -> RestoreRevisionResult
[Route("api/scenes/{sceneId:guid}/prose/revisions")]
public sealed class RevisionsController(ProseHistory history) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProseHistoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProseHistoryDto> List(Guid sceneId, CancellationToken ct) => history.ListAsync(sceneId, ct);

    [HttpPost("{revisionId:guid}/restore")]
    [ProducesResponseType<RestoreRevisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RestoreRevisionResult> Restore(Guid sceneId, Guid revisionId, CancellationToken ct)
        => history.RestoreAsync(sceneId, revisionId, ct);
}
