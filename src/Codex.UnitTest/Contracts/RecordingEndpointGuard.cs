using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Errors;

namespace Codex.UnitTest.Contracts;

// A guard that remembers which base URLs it was asked about and either allows them all or refuses them all.
public sealed class RecordingEndpointGuard(bool allow = true) : IAiEndpointGuard
{
    public IList<Uri> Checked { get; } = [];

    public bool LocalEndpointsAllowed => allow;

    public Task RequireAllowedAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        Checked.Add(baseUrl);
        return allow ? Task.CompletedTask : throw ApiException.Validation("That address is not allowed.");
    }
}
