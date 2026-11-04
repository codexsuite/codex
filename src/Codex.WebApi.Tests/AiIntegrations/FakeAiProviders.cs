using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;

namespace Codex.WebApi.Tests.AiIntegrations;

// IAiProviders double: records every connection it is asked about and answers with the scripted models, or throws the
// scripted ApiException (the provider's fixed-wording failure).
public sealed class FakeAiProviders : IAiProviders
{
    private readonly List<AiConnection> _connections = [];

    public IReadOnlyList<string> Models { get; set; } = ["model-b", "model-a"];

    public ApiException? Failure { get; set; }

    public IReadOnlyList<AiConnection> Connections
    {
        get
        {
            lock (_connections)
            {
                return _connections.ToList();
            }
        }
    }

    public AiConnection LastConnection => Connections[^1];

    public IProseGenerator Generator(AiConnection connection) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken)
    {
        lock (_connections)
        {
            _connections.Add(connection);
        }

        return Failure is { } failure ? throw failure : Task.FromResult(Models);
    }
}
