using Codex.Application.Features.Print;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// GET   /api/figures/{id}             -> FigureDto: the row behind a `::figure{id}` line (caption, placement, asset, web
//                                        rendition, effective dpi). 404 unknown or archived figure.
// POST  /api/scenes/{id}/figures      CreateFigureRequest -> CreateFigureResult; creates the figure row and saves the body
//                                        holding its ::figure line. 400 no line, a dropped embed, a bad placement or an image
//                                        outside the book and its series; 404 unknown scene or image; 409 stale version.
// PATCH /api/figures/{id}             PatchFigureRequest -> FigureDto. 404 unknown or archived figure, or unknown image.
// POST  /api/figures/{id}/remove      RemoveFigureRequest -> RemoveFigureResult; takes the line out, which archives the figure.
[Route("api")]
public sealed class FiguresController(IFigureService figures, FigureCommands commands) : ApiControllerBase
{
    [HttpGet("figures/{id:guid}")]
    [ProducesResponseType<FigureDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<FigureDto> Get(Guid id, CancellationToken ct) => figures.GetAsync(id, ct);

    [HttpPost("scenes/{sceneId:guid}/figures")]
    [ProducesResponseType<CreateFigureResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CreateFigureResult> Create(Guid sceneId, CreateFigureRequest request, CancellationToken ct) => commands.CreateAsync(sceneId, request, ct);

    [HttpPatch("figures/{id:guid}")]
    [ProducesResponseType<FigureDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FigureDto> Update(Guid id, PatchFigureRequest request, CancellationToken ct)
    {
        await commands.UpdateAsync(id, request, ct);
        return await figures.GetAsync(id, ct);
    }

    [HttpPost("figures/{id:guid}/remove")]
    [ProducesResponseType<RemoveFigureResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<RemoveFigureResult> Remove(Guid id, RemoveFigureRequest request, CancellationToken ct) => commands.RemoveAsync(id, request, ct);
}
