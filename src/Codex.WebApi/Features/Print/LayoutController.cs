using Codex.Application.Features.Print.Layout;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// GET  /api/books/{bookId}/layout           -> LayoutStateDto: the built-in templates (trims, options with the book's values) and the book's choice. 404 unknown book
// PUT  /api/books/{bookId}/layout           LayoutSelectionRequest -> LayoutStateDto: the book's template, trim and options. 404 unknown book or template; 400 bad trim or option
// POST /api/books/{bookId}/layout/preview   LayoutPreviewRequest -> LayoutPreviewDto: Quick preview HTML of a scene, chapter or book. 404 unknown book, scene or chapter;
//                                           400 bad scope, trim or option; 503 not_configured without pandoc
[Route("api/books/{bookId:guid}/layout")]
public sealed class LayoutController(ILayoutService layout) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<LayoutStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<LayoutStateDto> Get(Guid bookId, CancellationToken ct) => layout.GetAsync(bookId, ct);

    [HttpPut]
    [ProducesResponseType<LayoutStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<LayoutStateDto> Select(Guid bookId, LayoutSelectionRequest request, CancellationToken ct) => layout.SelectAsync(bookId, request, ct);

    [HttpPost("preview")]
    [ProducesResponseType<LayoutPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public Task<LayoutPreviewDto> Preview(Guid bookId, LayoutPreviewRequest request, CancellationToken ct) => layout.PreviewAsync(bookId, request, ct);
}
