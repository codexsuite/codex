using Codex.Application.Features.Settings;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Settings;

// GET /api/books/{id}/publication-settings -> PublicationSettingsDto (each value with `inherited`); unknown -> 404.
// PATCH …/publication-settings { style?, quoteWordLimit?, publisher?, plannedYear?, formats?, printRun?, territory? } -> PublicationSettingsDto.
//   Unknown style, limit <= 0, bad year/format, nothing to change -> 400.
// DELETE …/publication-settings/{field} ("This book ×": style, quoteWordLimit, publisher, plannedYear, formats, printRun, territory) -> PublicationSettingsDto.
// GET …/settings/rules-impact?limit= -> RulesImpactDto (writes nothing). GET …/settings/layout -> LayoutSettingsDto.
[Route("api/books")]
public sealed class PublicationController(PublicationSettingsCommands settings, RulesImpactQueries impact, LayoutSettingsQueries layout) : ApiControllerBase
{
    [HttpGet("{bookId:guid}/publication-settings")]
    [ProducesResponseType<PublicationSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PublicationSettingsDto> Get(Guid bookId, CancellationToken ct)
        => settings.GetAsync(bookId, ct);

    [HttpPatch("{bookId:guid}/publication-settings")]
    [ProducesResponseType<PublicationSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PublicationSettingsDto> Patch(Guid bookId, PatchPublicationSettingsRequest request, CancellationToken ct)
        => settings.PatchAsync(bookId, request, ct);

    [HttpDelete("{bookId:guid}/publication-settings/{field}")]
    [ProducesResponseType<PublicationSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PublicationSettingsDto> Reset(Guid bookId, string field, CancellationToken ct)
        => settings.ResetAsync(bookId, field, ct);

    [HttpGet("{bookId:guid}/settings/rules-impact")]
    [ProducesResponseType<RulesImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<RulesImpactDto> RulesImpact(Guid bookId, int? limit, CancellationToken ct)
        => impact.GetAsync(bookId, limit, ct);

    [HttpGet("{bookId:guid}/settings/layout")]
    [ProducesResponseType<LayoutSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<LayoutSettingsDto> Layout(Guid bookId, CancellationToken ct)
        => layout.GetAsync(bookId, ct);
}
