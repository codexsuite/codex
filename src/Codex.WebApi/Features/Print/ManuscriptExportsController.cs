using Codex.Application.Features.Print.Export;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// Export manuscript (J19): a Word, Markdown or EPUB copy that is not for print. The job, its progress, cancel and download
// are the ordinary /api/exports/{id} routes (ExportsController).
// GET  /api/books/{bookId}/manuscript-export  -> ManuscriptExportInfoDto: the live chapters and the at-risk source count. 404 unknown book
// POST /api/books/{bookId}/manuscript-exports ManuscriptExportRequest -> ExportDto (202): 404 unknown book or chapter; 400 a bad setting;
//                                              422 the book has no chapters; 409 one of this kind is already running
// GET  /api/books/{bookId}/manuscript-exports -> ExportListDto: docx, markdown and epub exports, newest first. 404 unknown book
[Route("api/books/{bookId:guid}")]
public sealed class ManuscriptExportsController(IManuscriptExportService exports) : ApiControllerBase
{
    [HttpGet("manuscript-export")]
    [ProducesResponseType<ManuscriptExportInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ManuscriptExportInfoDto> Info(Guid bookId, CancellationToken ct) => exports.InfoAsync(bookId, ct);

    [HttpPost("manuscript-exports")]
    [ProducesResponseType<ExportDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AcceptedResult> Queue(Guid bookId, ManuscriptExportRequest request, CancellationToken ct)
    {
        var export = await exports.RequestAsync(bookId, request, ct);
        return Accepted($"/api/exports/{export.Id}", export);
    }

    [HttpGet("manuscript-exports")]
    [ProducesResponseType<ExportListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ExportListDto> List(Guid bookId, CancellationToken ct) => exports.ListAsync(bookId, ct);
}
