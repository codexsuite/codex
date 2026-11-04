using Codex.Application.Features.Library;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Library;

// PATCH /api/series/{id} { name?, description? } -> SeriesDto. Blank name or nothing to change -> 400; unknown or
//   non-series id -> 404.
// POST /api/series/{id}/archive and /restore -> SeriesDto. A soft delete: the series' archived_at, with its books left as they are
//   (they read as archived while it is); restore needs an archived series, archive a live one -> else 404.
// PUT /api/series/{id}/order { bookIds } -> SeriesOrderDto. The list must be exactly the series' live books -> else 400.
[Route("api/series")]
public sealed class SeriesController(SeriesCommands series) : ApiControllerBase
{
    [HttpPatch("{seriesId:guid}")]
    [ProducesResponseType<SeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SeriesDto> Patch(Guid seriesId, PatchSeriesRequest request, CancellationToken ct)
        => series.PatchAsync(seriesId, request, ct);

    [HttpPut("{seriesId:guid}/order")]
    [ProducesResponseType<SeriesOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SeriesOrderDto> SetOrder(Guid seriesId, SetSeriesOrderRequest request, CancellationToken ct)
        => series.SetOrderAsync(seriesId, request, ct);

    [HttpPost("{seriesId:guid}/archive")]
    [ProducesResponseType<SeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<SeriesDto> Archive(Guid seriesId, CancellationToken ct)
    {
        await series.ArchiveAsync(seriesId, ct);
        return await series.GetAsync(seriesId, ct);
    }

    [HttpPost("{seriesId:guid}/restore")]
    [ProducesResponseType<SeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<SeriesDto> Restore(Guid seriesId, CancellationToken ct)
    {
        await series.RestoreAsync(seriesId, ct);
        return await series.GetAsync(seriesId, ct);
    }
}
