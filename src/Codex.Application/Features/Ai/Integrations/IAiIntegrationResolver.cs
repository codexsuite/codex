namespace Codex.Application.Features.Ai.Integrations;

// The current user's integration for a draft: the requested one (another user's is not found), else their default.
// Null when the user has none. RecordUseAsync stamps last_used_at and sets (or, with null, clears) last_error.
public interface IAiIntegrationResolver
{
    Task<ResolvedAiIntegration?> ResolveAsync(Guid? integrationId, CancellationToken cancellationToken);

    Task RecordUseAsync(Guid integrationId, string? error, CancellationToken cancellationToken);
}
