using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.KnowledgeBase;

// GET    /api/books/{id}/kb                    -> KbDto { categories, entries }. Unknown book 404 not_found.
// POST   /api/scenes/{id}/kb {entryId}         -> 204; linking twice is fine. Unknown scene or entry 404 not_found;
//                                                 entry outside the scene's book and series 400 validation.
// DELETE /api/scenes/{id}/kb/{entryId}         -> 204 (also when not linked). Unknown scene 404.
// PUT    /api/beats/{id}/context {entryIds}    -> 204; replaces the beat's whole context set. Same errors as POST.
[Route("api")]
public sealed class KnowledgeBaseController(IKbQueries queries, IKbLinks links) : ApiControllerBase
{
    [HttpGet("books/{id:guid}/kb")]
    [ProducesResponseType<KbDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbDto> Get(Guid id, CancellationToken ct) => queries.GetBookKbAsync(id, ct);

    [HttpPost("scenes/{id:guid}/kb")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> LinkScene(Guid id, LinkKbEntryRequest request, CancellationToken ct)
        => NoContentAfter(links.LinkSceneAsync(id, request.EntryId, ct));

    [HttpDelete("scenes/{id:guid}/kb/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> UnlinkScene(Guid id, Guid entryId, CancellationToken ct)
        => NoContentAfter(links.UnlinkSceneAsync(id, entryId, ct));

    [HttpPut("beats/{id:guid}/context")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> SetBeatContext(Guid id, SetBeatContextRequest request, CancellationToken ct)
        => NoContentAfter(links.SetBeatContextAsync(id, request.EntryIds, ct));
}
