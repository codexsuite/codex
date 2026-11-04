namespace Codex.Contracts.Events;

// The in-process event bus. Publishers call PublishAsync and move on: delivery to subscribers happens off
// the publishing request, in publish order per subscriber, so a slow handler never blocks a save.
// Subscribe(bookId, handler) delivers one book's events (null = every book) until the returned
// IDisposable is disposed; a handler that throws is logged and the subscription carries on.
// The SignalR transport (Codex.WebApi/Realtime/BookEventsRelay) subscribes here and forwards to the clients that joined the book.
public interface IBookEvents
{
    Task PublishAsync(BookEvent bookEvent);

    IDisposable Subscribe(Guid? bookId, Func<BookEvent, Task> handler);
}
