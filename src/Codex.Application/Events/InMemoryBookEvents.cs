using System.Collections.Immutable;
using System.Threading.Channels;
using System.Text.RegularExpressions;
using Codex.Contracts.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Events;

// One unbounded channel and one pump task per subscriber: Publish only enqueues, so it never waits for a
// handler. Disposing a subscription removes it before the next publish and stops its pump.
// With a notifier (the app always registers one), Publish also sends the event to the other processes on the database,
// after delivering it here; BookEventsListener hands what they send to DeliverFromElsewhere, which delivers it here only.
public sealed partial class InMemoryBookEvents(
    ILogger<InMemoryBookEvents> logger, IHttpContextAccessor? httpContext = null, BookEventsNotifier? notifier = null)
    : IBookEvents, IDisposable
{
    // The one place an event learns which tab caused it (the client sends this header on every request).
    public const string ClientHeader = "X-Codex-Client";

    private ImmutableList<Subscription> _subscriptions = [];

    public async Task PublishAsync(BookEvent bookEvent)
    {
        bookEvent = bookEvent with { ClientId = bookEvent.ClientId ?? RequestClientId() };
        Deliver(bookEvent);
        if (notifier != null)
        {
            await notifier.NotifyAsync(bookEvent);
        }
    }

    // An event another process published: to this process's subscribers, never sent on again. Its client id gets the
    // same check as a request's header, since anything that can NOTIFY on the database can send one.
    public void DeliverFromElsewhere(BookEvent bookEvent)
        => Deliver(bookEvent.ClientId is null || IsClientId(bookEvent.ClientId) ? bookEvent : bookEvent with { ClientId = null });

    public IDisposable Subscribe(Guid? bookId, Func<BookEvent, Task> handler)
    {
        var subscription = new Subscription(bookId, handler, logger, Remove);
        ImmutableInterlocked.Update(ref _subscriptions, list => list.Add(subscription));
        return subscription;
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    // Null without a request, without the header, or when the value is not a client id (max 64 of [A-Za-z0-9-]).
    private string? RequestClientId()
    {
        var value = httpContext?.HttpContext?.Request.Headers[ClientHeader].ToString();
        return IsClientId(value) ? value : null;
    }

    private static bool IsClientId(string? value) => value is not null && ClientIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ClientIdPattern();

    private void Deliver(BookEvent bookEvent)
    {
        foreach (var subscription in _subscriptions.Where(s => s.Wants(bookEvent)))
        {
            subscription.Enqueue(bookEvent);
        }
    }

    private void Remove(Subscription subscription)
        => ImmutableInterlocked.Update(ref _subscriptions, list => list.Remove(subscription));

    private sealed class Subscription : IDisposable
    {
        private readonly Guid? _bookId;
        private readonly Channel<BookEvent> _queue = Channel.CreateUnbounded<BookEvent>();
        private readonly CancellationTokenSource _stop = new();
        private readonly Action<Subscription> _remove;
        private int _disposed;

        public Subscription(Guid? bookId, Func<BookEvent, Task> handler, ILogger logger, Action<Subscription> remove)
        {
            _bookId = bookId;
            _remove = remove;
            _ = Task.Run(() => PumpAsync(handler, logger), _stop.Token);
        }

        public bool Wants(BookEvent bookEvent) => _bookId == null || _bookId == bookEvent.BookId;

        public void Enqueue(BookEvent bookEvent) => _queue.Writer.TryWrite(bookEvent);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
            _remove(this);
            _stop.Cancel();
            _queue.Writer.TryComplete();
            _stop.Dispose();
        }

        private async Task PumpAsync(Func<BookEvent, Task> handler, ILogger logger)
        {
            try
            {
                await foreach (var bookEvent in _queue.Reader.ReadAllAsync(_stop.Token))
                {
                    try
                    {
                        await handler(bookEvent);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogError(e, "A book event handler threw for {EventType}", bookEvent.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed: stop delivering.
            }
        }
    }
}
