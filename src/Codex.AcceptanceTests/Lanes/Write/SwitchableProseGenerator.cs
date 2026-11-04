using Codex.Contracts.Ai;

namespace Codex.AcceptanceTests.Lanes.Write;

// The one generator a draft scenario registers: each test sets the fake that answers it.
public sealed class SwitchableProseGenerator : IProseGenerator
{
    public IProseGenerator Current { get; set; } = null!;

    public IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, CancellationToken cancellationToken)
        => Current.GenerateAsync(request, cancellationToken);
}
