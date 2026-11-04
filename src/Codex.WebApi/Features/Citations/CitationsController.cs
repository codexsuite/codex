using Codex.Application.Features.Citations;
using Codex.Contracts.Citations;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Codex.WebApi.Features.Citations;

// POST /api/citations                -> CitePassageResult: wraps the passage as a source marker and adds its
//                                       reference_use. 404 unknown scene or source, 400 another book's source, an
//                                       unknown kind or a passage not in the body, 409 stale version.
// POST /api/citations/{useId}/remove -> RemoveCitationResult: unwraps the marker, the use is archived. 404 unknown
//                                       or archived use, 400 a use in a pending AI draft, 409 stale version.
// POST /api/quote-flags/{flagId}/cite -> CitePassageResult: wraps the flag's words in the scene's current prose and
//                                       marks the flag cited. 404 unknown flag, 409 a flag already resolved (or the
//                                       prose changed meanwhile), 400 no source or words no longer in the prose.
[Route("api")]
public sealed class CitationsController(ICitationCommands commands) : ApiControllerBase
{
    private const string Origin = "editor";

    [HttpPost("citations")]
    [ProducesResponseType<CitePassageResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CitePassageResult> Cite(CitePassageRequest request, CancellationToken ct) => commands.CitePassageAsync(request, Origin, ct);

    [HttpPost("citations/{useId:guid}/remove")]
    [ProducesResponseType<RemoveCitationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<RemoveCitationResult> Remove(Guid useId, RemoveCitationRequest request, CancellationToken ct) => commands.RemoveAsync(useId, request, Origin, ct);

    [HttpPost("quote-flags/{flagId:guid}/cite")]
    [ProducesResponseType<CitePassageResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CitePassageResult> CiteFlag(Guid flagId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CiteFlagRequest? request, CancellationToken ct)
        => commands.CiteFlagAsync(flagId, request ?? new CiteFlagRequest(), Origin, ct);
}
