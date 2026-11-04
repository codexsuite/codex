using Codex.Application.Features.Settings;
using Codex.Contracts.Errors;
using Codex.Contracts.Jobs;
using Codex.Contracts.Settings;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Settings;

// POST /api/books/{id}/duplicate { title } -> DuplicatedBookDto (References are not copied). Blank title -> 400; unknown book -> 404.
// POST /api/books/{id}/new-edition { title } -> DuplicatedBookDto: a duplicate that also copies the sources (sign-offs and permission requests cleared).
// POST /api/books/{id}/archive | /restore -> BookSettingsDto. Already archived / not archived -> 409; unknown -> 404.
// POST /api/books/{id}/export-source -> JobDto, a queued export_job of kind source (the export worker builds it); 409 while one is running.
[Route("api/books")]
public sealed class BookLifecycleController(BookLifecycleCommands lifecycle, BookSettingsCommands settings) : ApiControllerBase
{
    [HttpPost("{bookId:guid}/duplicate")]
    [ProducesResponseType<DuplicatedBookDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<DuplicatedBookDto> Duplicate(Guid bookId, DuplicateBookRequest request, CancellationToken ct)
        => lifecycle.DuplicateAsync(bookId, request, ct);

    [HttpPost("{bookId:guid}/new-edition")]
    [ProducesResponseType<DuplicatedBookDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<DuplicatedBookDto> NewEdition(Guid bookId, DuplicateBookRequest request, CancellationToken ct)
        => lifecycle.NewEditionAsync(bookId, request, ct);

    [HttpPost("{bookId:guid}/archive")]
    [ProducesResponseType<BookSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<BookSettingsDto> Archive(Guid bookId, CancellationToken ct)
    {
        await lifecycle.ArchiveAsync(bookId, ct);
        return await settings.GetAsync(bookId, ct);
    }

    [HttpPost("{bookId:guid}/restore")]
    [ProducesResponseType<BookSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<BookSettingsDto> Restore(Guid bookId, CancellationToken ct)
    {
        await lifecycle.RestoreAsync(bookId, ct);
        return await settings.GetAsync(bookId, ct);
    }

    [HttpPost("{bookId:guid}/export-source")]
    [ProducesResponseType<JobDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<JobDto> ExportSource(Guid bookId, CancellationToken ct)
        => lifecycle.QueueSourceExportAsync(bookId, ct);
}
