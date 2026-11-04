using System.Runtime.CompilerServices;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Ai;

// The production IProseGenerator (#36): the request names the user's AI integration (DraftGenerator picked it), this resolves it, hands its connection to the provider and passes the chunks through. A finished stream stamps the
// integration's last_used_at and clears last_error; a provider failure (always an ApiException with safe wording)
// is recorded as last_error and rethrown; a Stop records nothing. No integration means not_configured.
public sealed class IntegrationProseGenerator(IAiIntegrationResolver resolver, IAiProviders providers) : IProseGenerator
{
    public async IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.IntegrationId is not { } requested)
        {
            throw ApiException.NotConfigured("Connect an AI provider in Settings → AI to write drafts.");
        }

        var integration = await resolver.ResolveAsync(requested, cancellationToken)
            ?? throw ApiException.NotFound("AI integration not found.");
        await using var chunks = providers.Generator(integration.Connection).GenerateAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool more;
            try
            {
                more = await chunks.MoveNextAsync();
            }
            catch (ApiException e)
            {
                await resolver.RecordUseAsync(integration.Id, e.Message, CancellationToken.None);
                throw;
            }

            if (!more)
            {
                break;
            }

            yield return chunks.Current;
        }

        await resolver.RecordUseAsync(integration.Id, null, CancellationToken.None);
    }
}
