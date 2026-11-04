using System.Threading.Channels;
using Codex.Contracts.Events;

namespace Codex.WebApi.Tests.Events;

// Every event one host's bus delivers to a subscriber, in delivery order. "Nothing else arrived" is proved without
// waiting: a later marker event comes through the same path in order, so anything delivered before it shows first.
public sealed class EventCollector : IDisposable
{
    private readonly Channel<BookEvent> _events = Channel.CreateUnbounded<BookEvent>();
    private readonly IDisposable _subscription;

    public EventCollector(IBookEvents bus, Guid? bookId)
        => _subscription = bus.Subscribe(bookId, e => _events.Writer.WriteAsync(e).AsTask());

    public async Task<BookEvent> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _events.Reader.ReadAsync(timeout.Token);
    }

    public void Dispose() => _subscription.Dispose();
}
