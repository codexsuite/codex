using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.References;

// GET   /api/references/{id}                  -> SourceDetailDto. Unknown or archived source 404 not_found.
// POST  /api/references/{id}/sign-off         -> SourceDetailDto. 422 blocked while a copyright risk is open.
// POST  /api/references/{id}/reopen           -> SourceDetailDto.
// POST  /api/references/{id}/fair-use         {reason} -> SourceDetailDto. 400 validation for a blank reason or a licence that needs no decision.
// PATCH /api/references/{id}                  {details?, entry?} -> SourceDetailDto. 400 validation for a bad kind, licence, author, date or an entry outside the book's scope.
// PATCH /api/reference-uses/{id}              {kind} -> SourceDetailDto. 400 validation for an unknown kind; 404 for an unknown use.
// POST  /api/references/{id}/recheck          -> RecheckResultDto, through ISourceLookup. 503 not_configured until a provider is registered.
// POST  /api/references/{id}/recheck/accept   {redirectTo} -> SourceDetailDto. 400 validation when no re-check reported that address.
// POST  /api/references/{id}/archive          -> ArchiveSourceResult. Uses become plain text through IProseStore first.
// DELETE /api/references/{id}                 -> the same as archive (rule R4: convert, never block): markers become plain text with a
//                                                revision per changed scene, the uses and the source are archived. Nothing is hard-deleted.
// POST  /api/references/{id}/merge            {intoId} -> SourceDetailDto of the target. 400 into itself or across books; 422 when both have a live permission request.
[Route("api")]
public sealed class SourceDetailController(ISourceDetailQueries queries, ISourceCommands commands) : ApiControllerBase
{
    [HttpGet("references/{id:guid}")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);

    [HttpPost("references/{id:guid}/sign-off")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SourceDetailDto> SignOff(Guid id, CancellationToken ct) => commands.SignOffAsync(id, ct);

    [HttpPost("references/{id:guid}/reopen")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> Reopen(Guid id, CancellationToken ct) => commands.ReopenAsync(id, ct);

    [HttpPost("references/{id:guid}/fair-use")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> AcceptFairUse(Guid id, FairUseRequest request, CancellationToken ct) => commands.AcceptFairUseAsync(id, request, ct);

    [HttpPatch("references/{id:guid}")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> Patch(Guid id, PatchSourceRequest request, CancellationToken ct) => commands.PatchAsync(id, request, ct);

    [HttpPatch("reference-uses/{id:guid}")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> SetUseKind(Guid id, PatchUseRequest request, CancellationToken ct) => commands.SetUseKindAsync(id, request, ct);

    [HttpPost("references/{id:guid}/recheck")]
    [ProducesResponseType<RecheckResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public Task<RecheckResultDto> Recheck(Guid id, CancellationToken ct) => commands.RecheckAsync(id, ct);

    [HttpPost("references/{id:guid}/recheck/accept")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SourceDetailDto> AcceptRedirect(Guid id, AcceptRedirectRequest request, CancellationToken ct) => commands.AcceptRedirectAsync(id, request, ct);

    [HttpPost("references/{id:guid}/archive")]
    [ProducesResponseType<ArchiveSourceResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ArchiveSourceResult> Archive(Guid id, CancellationToken ct) => commands.ArchiveAsync(id, ct);

    [HttpDelete("references/{id:guid}")]
    [ProducesResponseType<ArchiveSourceResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ArchiveSourceResult> Delete(Guid id, CancellationToken ct) => commands.ArchiveAsync(id, ct);

    [HttpPost("references/{id:guid}/merge")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SourceDetailDto> Merge(Guid id, MergeSourceRequest request, CancellationToken ct) => commands.MergeAsync(id, request, ct);
}
