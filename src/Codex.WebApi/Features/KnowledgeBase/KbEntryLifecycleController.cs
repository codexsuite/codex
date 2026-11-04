using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Codex.WebApi.Features.KnowledgeBase;

// POST   /api/kb/entries/{id}/related            {entryId} -> KbEntryDto. Self or an entry outside the scope 400 validation;
//                                                an unknown or deleted entry 404. Linking twice is fine.
// DELETE /api/kb/entries/{id}/related/{otherId}  -> KbEntryDto (unlinking a missing pair is fine).
// POST   /api/kb/entries/{id}/make-mention       {sceneId} -> KbEntryDto. The first plain whole-word use of the name (or an
//                                                alias) in the scene's prose becomes a mention; none that can be wrapped 400.
// POST   /api/kb/entries/{id}/promote            {merge?} -> KbEntryDto of the series entry. "Move to series": the entry becomes the
//                                                series' (same id). A book outside a series 400; an entry already in the series 409;
//                                                a series entry with the same name 409 needs_confirmation unless merge, which folds
//                                                this entry into that one and returns it.
// GET    /api/kb/entries/{id}/delete-impact      -> KbDeleteImpactDto (mentions, scenes, notes, links).
// DELETE /api/kb/entries/{id}                    -> KbUndoRecordDto. Soft delete; a second delete 404.
// POST   /api/kb/entries/{id}/restore            KbUndoRecordDto -> KbEntryDto. A scene, summary, beat or entry text that
//                                                changed since the delete 409; an entry that is not deleted 404.
[Route("api/kb/entries")]
public sealed class KbEntryLifecycleController(
    IKbRelatedCommands related,
    IKbMentionCommands mentions,
    IKbPromoteCommands promote,
    IKbDeleteCommands deletes) : ApiControllerBase
{
    [HttpPost("{id:guid}/related")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbEntryDto> AddRelated(Guid id, AddKbRelatedRequest request, CancellationToken ct) => related.AddAsync(id, request.EntryId, ct);

    [HttpDelete("{id:guid}/related/{otherId:guid}")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbEntryDto> RemoveRelated(Guid id, Guid otherId, CancellationToken ct) => related.RemoveAsync(id, otherId, ct);

    [HttpPost("{id:guid}/make-mention")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbEntryDto> MakeMention(Guid id, MakeKbMentionRequest request, CancellationToken ct) => mentions.MakeMentionAsync(id, request.SceneId, ct);

    [HttpPost("{id:guid}/promote")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<KbEntryDto> Promote(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PromoteKbEntryRequest? request, CancellationToken ct)
        => promote.PromoteAsync(id, request?.Merge ?? false, ct);

    [HttpGet("{id:guid}/delete-impact")]
    [ProducesResponseType<KbDeleteImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbDeleteImpactDto> DeleteImpact(Guid id, CancellationToken ct) => deletes.ImpactAsync(id, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<KbUndoRecordDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbUndoRecordDto> Delete(Guid id, CancellationToken ct) => deletes.DeleteAsync(id, ct);

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<KbEntryDto> Restore(Guid id, KbUndoRecordDto record, CancellationToken ct) => deletes.RestoreAsync(id, record, ct);
}
