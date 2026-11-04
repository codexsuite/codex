using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.References;

// GET /api/books/{id}/sources            -> SourceRowDto[] (every live source, marker order, uncited last). Unknown book 404.
// GET /api/books/{id}/sources/csl        -> SourceCslDto[] (cited sources only, for the bibliography). Unknown book 404.
// GET /api/books/{id}/sources/export?format=bibtex|ris|csljson|permissions
//                                        -> the file, as an attachment. Exports cover every live source; the
//                                           permissions log covers sources that need or have a request. Unknown
//                                           format 400 validation; unknown book 404.
[Route("api/books/{id:guid}/sources")]
public sealed class SourcesController(ISourceQueries queries) : ApiControllerBase
{
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<SourceRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<SourceRowDto>> List(Guid id, CancellationToken ct) => queries.GetSourcesAsync(id, ct);

    [HttpGet("csl")]
    [ProducesResponseType<IReadOnlyList<SourceCslDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<SourceCslDto>> Csl(Guid id, CancellationToken ct) => queries.GetCslAsync(id, ct);

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FileContentResult> Export(Guid id, string? format, CancellationToken ct)
    {
        var file = await queries.ExportAsync(id, format, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
