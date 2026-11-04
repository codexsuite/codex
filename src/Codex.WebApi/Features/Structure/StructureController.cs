using Codex.Application.Features.Structure;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Structure;

// GET    /api/books/{bookId:guid}/outline -> OutlineDto
// PATCH  /api/scenes/{sceneId:guid}       -> OutlineSceneDto
// PUT    /api/scenes/{sceneId:guid}/labels -> LabelRefDto[]
// GET    /api/books/{bookId:guid}/labels  -> LabelDto[]
// POST   /api/books/{bookId:guid}/labels  -> 201 LabelDto (Location /api/labels/{id})
// PATCH  /api/labels/{labelId:guid}       -> LabelDto
// DELETE /api/labels/{labelId:guid}       -> 204
[Route("api")]
public sealed class StructureController(OutlineQuery outline, SceneCommands scenes, LabelCommands labels) : ApiControllerBase
{
    [HttpGet("books/{bookId:guid}/outline")]
    [ProducesResponseType<OutlineDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineDto> Outline(Guid bookId, CancellationToken ct) => outline.GetAsync(bookId, ct);

    [HttpPatch("scenes/{sceneId:guid}")]
    [ProducesResponseType<OutlineSceneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineSceneDto> PatchScene(Guid sceneId, PatchSceneRequest request, CancellationToken ct) => scenes.PatchAsync(sceneId, request, ct);

    [HttpPut("scenes/{sceneId:guid}/labels")]
    [ProducesResponseType<IReadOnlyList<LabelRefDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<LabelRefDto>> SetSceneLabels(Guid sceneId, SetSceneLabelsRequest request, CancellationToken ct)
        => scenes.SetLabelsAsync(sceneId, request, ct);

    [HttpGet("books/{bookId:guid}/labels")]
    [ProducesResponseType<IReadOnlyList<LabelDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<LabelDto>> ListLabels(Guid bookId, CancellationToken ct) => labels.ListAsync(bookId, ct);

    [HttpPost("books/{bookId:guid}/labels")]
    [ProducesResponseType<LabelDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> CreateLabel(Guid bookId, CreateLabelRequest request, CancellationToken ct)
    {
        var created = await labels.CreateAsync(bookId, request, ct);
        return Created($"/api/labels/{created.Id}", created);
    }

    [HttpPatch("labels/{labelId:guid}")]
    [ProducesResponseType<LabelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<LabelDto> PatchLabel(Guid labelId, PatchLabelRequest request, CancellationToken ct) => labels.PatchAsync(labelId, request, ct);

    [HttpDelete("labels/{labelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> DeleteLabel(Guid labelId, CancellationToken ct) => NoContentAfter(labels.DeleteAsync(labelId, ct));
}
