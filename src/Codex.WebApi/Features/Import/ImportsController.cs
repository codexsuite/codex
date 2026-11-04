using Codex.Application.Features.Import;
using Codex.Contracts.Errors;
using Codex.Contracts.Import;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Import;

// POST /api/imports/preview      multipart: source, files (each part's file name is its path in the folder), and the
//                                paths the client left out as notMarkdown / tooLarge -> ImportPreviewDto
// POST /api/imports              same body -> 201 ImportSummaryDto; the book, its structure and prose in one transaction
// GET  /api/books/{id}/import    -> BookImportDto (the banner, or null). 404 for an unknown book.
// POST /api/imports/{id}/dismiss -> 204, hides the banner. 404 unknown.
// POST /api/imports/{id}/undo    -> 204, deletes the imported book. 404 unknown or already undone.
// 400 validation: not multipart, no files, over ImportLimits (count, total size), a path that is not relative to the
// folder, an unknown source, or files that are not the source's format. Single oversize files are skipped instead.
// A source that places images (pandoc) also takes the image files its preview listed (ImportPreviewDto.Images): they
// are not counted in the Markdown total and never read into memory here (the form reader spools them to disk); the
// asset intake reads each once. POST /api/imports allows ImportLimits.MaxImageTotalBytes of them.
[Route("api")]
public sealed class ImportsController(ImportService imports) : ApiControllerBase
{
    [HttpPost("imports/preview")]
    [RequestSizeLimit(ImportLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportLimits.MaxRequestBytes, ValueCountLimit = ImportUploadReader.MaxFormValues)]
    [ProducesResponseType<ImportPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ImportPreviewDto> Preview(CancellationToken ct) => imports.Preview(await ImportUploadReader.ReadAsync(Request, ct));

    [HttpPost("imports")]
    [RequestSizeLimit(ImportLimits.MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportLimits.MaxImportRequestBytes, ValueCountLimit = ImportUploadReader.MaxFormValues)]
    [ProducesResponseType<ImportSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<CreatedResult> Import(CancellationToken ct)
    {
        var summary = await imports.ImportAsync(await ImportUploadReader.ReadAsync(Request, ct), ct);
        return Created($"/api/books/{summary.BookId}/import", summary);
    }

    [HttpGet("books/{bookId:guid}/import")]
    [ProducesResponseType<BookImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookImportDto> Banner(Guid bookId, CancellationToken ct) => imports.GetBannerAsync(bookId, ct);

    [HttpPost("imports/{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> Dismiss(Guid id, CancellationToken ct) => NoContentAfter(imports.DismissAsync(id, ct));

    [HttpPost("imports/{id:guid}/undo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> Undo(Guid id, CancellationToken ct) => NoContentAfter(imports.UndoAsync(id, ct));
}
