using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;

namespace Codex.TestSupport.Fakes;

// An IAiProviders for tests: every connection it is asked to generate with is recorded, and the stream comes from
// `Next` (a FakeProseGenerator by default), which a test can swap to script a failure. No network.
public sealed class RecordingAiProviders(IProseGenerator? next = null) : IAiProviders
{
    private readonly List<AiConnection> _connections = [];

    public IProseGenerator Next { get; set; } = next ?? new FakeProseGenerator(["Some words. ", "More words."]);

    public IReadOnlyList<AiConnection> Connections => _connections;

    public IProseGenerator Generator(AiConnection connection)
    {
        _connections.Add(connection);
        return Next;
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
