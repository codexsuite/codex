using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.References;

// POST /api/books/{id}/references/lookup {kind, value} -> LookupSourceResultDto. Outcomes existing (already in the book),
//                                            found, redirect, not_found. 400 validation for a bad kind, ISBN, DOI or a private address.
// POST /api/books/{id}/references {details, via?}      -> AddSourceResult: 201 with Id, or 200 with ExistingId when the
//                                            ISBN, DOI, URL or cite key is already there. 400 for a blank title or bad field.
// POST /api/books/{id}/references/import {items}       -> ImportSourcesResult (duplicates skipped). Providers bibtex, ris, csl. 400 above 500 items, 413 above 2 MB.
// POST /api/books/{id}/references/import/preview {items} -> the same result, written nowhere.
// An unknown or archived book is 404 not_found on all four.
[Route("api/books/{bookId:guid}/references")]
public sealed class AddSourceController(IAddSourceCommands commands) : ApiControllerBase
{
    private const long MaxImportBodyBytes = 2_097_152;

    [HttpPost("lookup")]
    [ProducesResponseType<LookupSourceResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<LookupSourceResultDto> Lookup(Guid bookId, LookupSourceRequest request, CancellationToken ct) => commands.LookupAsync(bookId, request, ct);

    [HttpPost("")]
    [ProducesResponseType<AddSourceResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<AddSourceResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ObjectResult> Add(Guid bookId, AddSourceRequest request, CancellationToken ct)
    {
        var result = await commands.AddAsync(bookId, request, ct);
        return StatusCode(result.Id is null ? StatusCodes.Status200OK : StatusCodes.Status201Created, result);
    }

    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBodyBytes)]
    [ProducesResponseType<ImportSourcesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ImportSourcesResult> Import(Guid bookId, ImportSourcesRequest request, CancellationToken ct) => commands.ImportAsync(bookId, request, ct);

    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxImportBodyBytes)]
    [ProducesResponseType<ImportSourcesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ImportSourcesResult> PreviewImport(Guid bookId, ImportSourcesRequest request, CancellationToken ct) => commands.PreviewImportAsync(bookId, request, ct);
}
