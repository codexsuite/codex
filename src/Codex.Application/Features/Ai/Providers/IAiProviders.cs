using Codex.Contracts.Ai;

namespace Codex.Application.Features.Ai.Providers;

// The provider side of an integration (#36): a generator for a connection, and the connection's model list. Every
// failure is an ApiException with fixed wording (never the provider's body, never the key): 401/403 not_configured
// ("the key was rejected"), 402 / no credit not_configured with its own message, 429 / 5xx / network internal_error,
// a refused base URL validation. A user-supplied base URL passes IAiEndpointGuard before every request.
public interface IAiProviders
{
    IProseGenerator Generator(AiConnection connection);

    // The provider's model ids, sorted. Empty when the provider answers but has no model list.
    Task<IReadOnlyList<string>> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken);
}
