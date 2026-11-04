using Codex.Application.Features.Library;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Library;

// POST /api/books { title, subtitle?, seriesId?, seriesNumber?, targetWords?, status? } -> 201 CreatedBookDto.
//   Blank title, a seriesId that is not a live series, a number without a series, a bad status -> 400;
//   a number already used in the series -> 409. Omit seriesNumber in a series to take the next free one.
// GET /api/series/{id}/next-number -> NextSeriesNumberDto (highest number + 1, archived books counted); unknown -> 404.
[Route("api")]
public sealed class BooksController(BookCommands books) : ApiControllerBase
{
    [HttpPost("books")]
    [ProducesResponseType<CreatedBookDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> Create(CreateBookRequest request, CancellationToken ct)
    {
        var created = await books.CreateAsync(request, ct);
        return Created($"/api/books/{created.Id}", created);
    }

    [HttpGet("series/{seriesId:guid}/next-number")]
    [ProducesResponseType<NextSeriesNumberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NextSeriesNumberDto> NextNumber(Guid seriesId, CancellationToken ct)
        => books.NextNumberAsync(seriesId, ct);
}
