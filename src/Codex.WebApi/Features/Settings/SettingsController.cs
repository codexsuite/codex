using Codex.Application.Features.Settings;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Settings;

// GET /api/books/{id}/settings -> BookSettingsDto (book, series and writing sections in one response); unknown -> 404.
// PATCH /api/books/{id}/settings { title?, authorName?, status?, targetWords?, styleGuide? } -> BookSettingsDto.
//   Nothing to change, blank title, unknown status or negative target -> 400.
// PUT /api/books/{id}/cover { assetId } -> 204. The book's (or its series') image becomes the front cover. No assetId,
//   an asset outside the book's scope or not an image -> 400; unknown book or unknown/archived asset -> 404.
// DELETE /api/books/{id}/cover -> 204: back to the generated cover. Unknown book -> 404.
// PUT /api/books/{id}/series-number { number } -> SeriesNumberChangeDto. Out of 1..n, standalone or archived book -> 400.
// POST /api/books/{id}/series-move/preview { seriesId|null } -> SeriesMovePreviewDto (writes nothing).
// POST /api/books/{id}/series-move { seriesId|null } -> BookSettingsDto after the move. A target that is not a live
//   series, or the book's current series -> 400.
[Route("api/books")]
public sealed class SettingsController(BookSettingsCommands settings, BookCoverCommands covers, SeriesNumberCommands numbers, SeriesMoveCommands moves) : ApiControllerBase
{
    [HttpGet("{bookId:guid}/settings")]
    [ProducesResponseType<BookSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookSettingsDto> Get(Guid bookId, CancellationToken ct)
        => settings.GetAsync(bookId, ct);

    [HttpPatch("{bookId:guid}/settings")]
    [ProducesResponseType<BookSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookSettingsDto> Patch(Guid bookId, PatchBookSettingsRequest request, CancellationToken ct)
        => settings.PatchAsync(bookId, request, ct);

    [HttpPut("{bookId:guid}/cover")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> SetCover(Guid bookId, SetBookCoverRequest request, CancellationToken ct)
        => NoContentAfter(covers.SetAsync(bookId, request, ct));

    [HttpDelete("{bookId:guid}/cover")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> ClearCover(Guid bookId, CancellationToken ct)
        => NoContentAfter(covers.ClearAsync(bookId, ct));

    [HttpPut("{bookId:guid}/series-number")]
    [ProducesResponseType<SeriesNumberChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SeriesNumberChangeDto> SetSeriesNumber(Guid bookId, SetSeriesNumberRequest request, CancellationToken ct)
        => numbers.SetAsync(bookId, request, ct);

    [HttpPost("{bookId:guid}/series-move/preview")]
    [ProducesResponseType<SeriesMovePreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SeriesMovePreviewDto> PreviewSeriesMove(Guid bookId, SeriesMoveRequest request, CancellationToken ct)
        => moves.PreviewAsync(bookId, request, ct);

    [HttpPost("{bookId:guid}/series-move")]
    [ProducesResponseType<BookSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<BookSettingsDto> MoveSeries(Guid bookId, SeriesMoveRequest request, CancellationToken ct)
    {
        await moves.MoveAsync(bookId, request, ct);
        return await settings.GetAsync(bookId, ct);
    }
}
