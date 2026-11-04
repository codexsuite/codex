using Codex.Contracts.References;

namespace Codex.Application.Features.References;

// `provenance` says who a History line is written as; null is the author ('user'), the MCP tools pass an agent.
// Every write on a source answers with the refreshed panel. Unknown or archived source: ApiException.NotFound.
public interface ISourceCommands
{
    // Blocked (422) while a copyright risk is open.
    Task<SourceDetailDto> SignOffAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    Task<SourceDetailDto> ReopenAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Validation (400) for a blank reason or a licence that needs no decision.
    Task<SourceDetailDto> AcceptFairUseAsync(Guid sourceId, FairUseRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Validation (400) for an unknown kind or licence, a bad author or date, or a Codex entry outside the book's scope.
    Task<SourceDetailDto> PatchAsync(Guid sourceId, PatchSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    Task<SourceDetailDto> SetUseKindAsync(Guid useId, PatchUseRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Looks up the source's ISBN, DOI and URL through ISourceLookup and stores the answers as its checks.
    Task<RecheckResultDto> RecheckAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Validation (400) when no re-check reported that address.
    Task<SourceDetailDto> AcceptRedirectAsync(Guid sourceId, AcceptRedirectRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Rule R4 (a source with uses is converted, never blocked): turns every use back into plain text through IProseStore,
    // which cuts a revision per changed scene, then archives the uses and the source. Open quote flags lose their suggested source.
    Task<ArchiveSourceResult> ArchiveAsync(Guid sourceId, CancellationToken cancellationToken, EventProvenance? provenance = null);

    // Folds a duplicate into `request.IntoId`: its uses, permission request, attachments and quote flags move over, the
    // target takes the fields it lacks, and the duplicate is archived with merged_into_id set. Answers with the target.
    // Validation (400) for a merge into itself or across books; Blocked (422) when both have a live permission request.
    Task<SourceDetailDto> MergeAsync(Guid sourceId, MergeSourceRequest request, CancellationToken cancellationToken, EventProvenance? provenance = null);
}
