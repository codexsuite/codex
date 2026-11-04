using System.Threading.Channels;
using Agoda.IoC.Core;

namespace Codex.Application.Features.Print;

// Wakes the rendition worker when a job is queued in this process; jobs queued elsewhere are found by its poll.
[RegisterSingleton]
public sealed class AssetRenditionSignal
{
    private readonly Channel<bool> _queued = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Notify() => _queued.Writer.TryWrite(true);

    // Returns when notified, after timeout (on the clock), or when cancelled by cancellationToken (which throws).
    public async Task WaitAsync(TimeSpan timeout, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var timer = new CancellationTokenSource(timeout, clock);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        try
        {
            await _queued.Reader.ReadAsync(either.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // timed out: poll again
        }
    }
}
