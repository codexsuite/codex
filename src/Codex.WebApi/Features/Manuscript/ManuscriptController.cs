using Codex.Application.Features.Manuscript;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Manuscript;

// GET /api/scenes/{sceneId:guid}/prose -> ProseDto
// PUT /api/scenes/{sceneId:guid}/prose SaveProseRequest -> SaveProseResult
[Route("api/scenes/{sceneId:guid}/prose")]
public sealed class ManuscriptController(IProseStore prose, EditorProseSave save) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProseDto> Get(Guid sceneId, CancellationToken ct) => prose.GetAsync(sceneId, ct);

    [HttpPut]
    [ProducesResponseType<SaveProseResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SaveProseResult> Save(Guid sceneId, SaveProseRequest request, CancellationToken ct) => save.SaveAsync(sceneId, request, ct);
}
