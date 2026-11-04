namespace Codex.Application.Features.Ai.Integrations;

// The SSRF check on a user-supplied base URL, run when an integration is saved or tested and again before every
// provider request (DNS can change between the two). Throws ApiException.Validation for a URL that is not https://,
// or whose host resolves to a private, loopback, link-local or metadata address, unless the server owner has set
// CODEX_AI_ALLOW_LOCAL_ENDPOINTS.
public interface IAiEndpointGuard
{
    bool LocalEndpointsAllowed { get; }

    Task RequireAllowedAsync(Uri baseUrl, CancellationToken cancellationToken);
}
