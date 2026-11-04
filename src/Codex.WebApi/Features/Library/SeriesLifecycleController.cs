using Codex.Application.Features.Library;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Library;

// POST   /api/series { name, bookIds?, description?, authorName? } -> 201 CreatedSeriesDto. The books must be the caller's standalone, unarchived
//   books; they get Book 001.. in the order given. Blank name, a repeated or unsuitable book -> 400; an empty list is fine.
// GET    /api/series/{id}/delete-impact -> SeriesDeleteImpactDto. Unknown -> 404.
// DELETE /api/series/{id} -> 204. Every book becomes standalone with the series' Knowledge base and labels copied onto it
//   (book_copy_down_series_scope), then the series is removed. Unknown or not a series -> 404.
[Route("api/series")]
public sealed class SeriesLifecycleController(SeriesLifecycleCommands series) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreatedSeriesDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<CreatedResult> Create(CreateSeriesRequest request, CancellationToken ct)
    {
        var created = await series.CreateAsync(request, ct);
        return Created($"/api/series/{created.Id}", created);
    }

    [HttpGet("{seriesId:guid}/delete-impact")]
    [ProducesResponseType<SeriesDeleteImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SeriesDeleteImpactDto> Impact(Guid seriesId, CancellationToken ct)
        => series.ImpactAsync(seriesId, ct);

    [HttpDelete("{seriesId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> Delete(Guid seriesId, CancellationToken ct)
        => NoContentAfter(series.DeleteAsync(seriesId, ct));
}
