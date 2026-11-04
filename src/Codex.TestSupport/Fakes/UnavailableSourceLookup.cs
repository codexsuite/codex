using Codex.Contracts.Errors;
using Codex.Contracts.Sources;

namespace Codex.TestSupport.Fakes;

// A server with no look-up provider: every look-up fails with `not_configured` (503).
public sealed class UnavailableSourceLookup : ISourceLookup
{
    public Task<SourceLookupResult> LookupAsync(string kind, string value, CancellationToken cancellationToken)
        => throw ApiException.NotConfigured("Source look-up is not configured on this server, so identifiers cannot be re-checked yet.");
}
