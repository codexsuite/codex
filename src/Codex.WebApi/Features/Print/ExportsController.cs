using Codex.Application.Features.Print.Export;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// POST /api/books/{bookId}/export/preflight   ExportRequest -> PreflightDto: what Export for printer would find now, queuing nothing.
//                                              404 unknown book; 400 a bad bleed, quality, format, trim or option
// POST /api/books/{bookId}/exports            ExportRequest -> ExportDto (202): queues the whole book. 404 unknown book; 400 a bad setting;
//                                              422 blocked (blocking preflight items, and the request is not a draft); 409 one is already running
// GET  /api/books/{bookId}/exports            -> ExportListDto: print and source exports, newest first, and the running one. 404 unknown book
// GET  /api/exports/{id}                      -> ExportDto with its log. 404 unknown export
// POST /api/exports/{id}/cancel               -> ExportDto: cancels a queued or running export. 404 unknown; 409 already finished
// GET  /api/exports/{id}/file                 -> the PDF or source .zip as a download. 404 unknown export or file; 409 not finished
// Exports are built by the worker (CODEX_ROLE worker or all), so a web-only host queues them and never renders.
[Route("api")]
public sealed class ExportsController(IExportService exports) : ApiControllerBase
{
    [HttpPost("books/{bookId:guid}/export/preflight")]
    [ProducesResponseType<PreflightDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PreflightDto> Preflight(Guid bookId, ExportRequest request, CancellationToken ct) => exports.PreflightAsync(bookId, request, ct);

    [HttpPost("books/{bookId:guid}/exports")]
    [ProducesResponseType<ExportDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AcceptedResult> Queue(Guid bookId, ExportRequest request, CancellationToken ct)
    {
        var export = await exports.RequestAsync(bookId, request, ct);
        return Accepted($"/api/exports/{export.Id}", export);
    }

    [HttpGet("books/{bookId:guid}/exports")]
    [ProducesResponseType<ExportListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ExportListDto> List(Guid bookId, CancellationToken ct) => exports.ListAsync(bookId, ct);

    [HttpGet("exports/{id:guid}")]
    [ProducesResponseType<ExportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ExportDto> Get(Guid id, CancellationToken ct) => exports.GetAsync(id, ct);

    [HttpPost("exports/{id:guid}/cancel")]
    [ProducesResponseType<ExportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ExportDto> Cancel(Guid id, CancellationToken ct) => exports.CancelAsync(id, ct);

    [HttpGet("exports/{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<FileStreamResult> DownloadFile(Guid id, CancellationToken ct)
    {
        var file = await exports.OpenFileAsync(id, ct);
        return File(file.Content.Content, file.Content.ContentType, file.Filename);
    }
}
