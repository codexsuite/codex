using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.KnowledgeBase;

// GET    /api/kb/entries/{id}/alias-check?alias=   -> KbAliasCheckDto: the other entry that already has the alias (null for none),
//                                                  its whole-word matches in the prose, and whether that is a lot (3 letters or fewer, 25+ matches).
// GET    /api/kb/entries/{id}/merge-impact?intoId= -> KbMergeImpactDto: what merging moves (scene links, additions, related, new aliases).
// POST   /api/kb/entries/{id}/merge   {intoId}     -> KbEntryDto of the target. Moves the entry's uses to the target, adds its names as
//                                                  aliases and archives it. Itself or a target out of scope 400; unknown entry 404.
// PATCH  /api/kb/categories/{id}      {name?, colour?} -> KbCategoryDto. A name the book or series already has 409; unknown colour 400.
// GET    /api/kb/categories/{id}/delete-impact -> KbCategoryDeleteImpactDto (live entries in it).
// DELETE /api/kb/categories/{id}      -> 204. A category with live entries 409 ("Move or delete its N entries first.").
// Every write publishes StructureChanged(book or series, "kb").
[Route("api/kb")]
public sealed class KbMaintenanceController(IKbAliasQueries aliases, IKbMergeCommands merges, IKbCategoryCommands categories) : ApiControllerBase
{
    [HttpGet("entries/{id:guid}/alias-check")]
    [ProducesResponseType<KbAliasCheckDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbAliasCheckDto> AliasCheck(Guid id, string? alias, CancellationToken ct) => aliases.CheckAsync(id, alias, ct);

    [HttpGet("entries/{id:guid}/merge-impact")]
    [ProducesResponseType<KbMergeImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbMergeImpactDto> MergeImpact(Guid id, Guid? intoId, CancellationToken ct) => merges.ImpactAsync(id, intoId ?? Guid.Empty, ct);

    [HttpPost("entries/{id:guid}/merge")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbEntryDto> Merge(Guid id, KbMergeRequest request, CancellationToken ct) => merges.MergeAsync(id, request.IntoId, ct);

    [HttpPatch("categories/{id:guid}")]
    [ProducesResponseType<KbCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<KbCategoryDto> PatchCategory(Guid id, PatchKbCategoryRequest request, CancellationToken ct) => categories.PatchAsync(id, request, ct);

    [HttpGet("categories/{id:guid}/delete-impact")]
    [ProducesResponseType<KbCategoryDeleteImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbCategoryDeleteImpactDto> CategoryDeleteImpact(Guid id, CancellationToken ct) => categories.ImpactAsync(id, ct);

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<NoContentResult> DeleteCategory(Guid id, CancellationToken ct) => NoContentAfter(categories.DeleteAsync(id, ct));
}
