using System.Text.Json;
using Codex.Contracts.Events;
using Microsoft.AspNetCore.SignalR;

namespace Codex.WebApi.Realtime;

// Subscribes to the in-process bus for every book (other processes' events included, through the Postgres bridge) and
// forwards each event to the clients that joined that book. The bus delivers off the publishing request and in publish
// order, so a slow client never blocks a save.
//
// AccessChanged (#80) is the exception: it is about one user, so it goes to that user's connections (Clients.User, by
// the NameIdentifier claim) and never to the book's group. When the user can no longer open the book (Role null), their
// connections leave its group first, so nothing from the book reaches them after the event.
public sealed class BookEventsRelay(IBookEvents events, IHubContext<BookHub> hub, BookGroupMembers members) : IHostedService
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(null, ForwardAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ForwardAsync(BookEvent bookEvent)
    {
        if (bookEvent is not AccessChanged changed)
        {
            await hub.Clients.Group(BookHub.GroupName(bookEvent.BookId)).SendAsync(BookHub.EventMethod, Payload(bookEvent));
            return;
        }
        if (changed.Role is null)
        {
            foreach (var connectionId in members.Remove(changed.UserId, changed.BookId))
            {
                await hub.Groups.RemoveFromGroupAsync(connectionId, BookHub.GroupName(changed.BookId));
            }
        }
        await hub.Clients.User(changed.UserId.ToString()).SendAsync(BookHub.EventMethod, Payload(changed));
    }

    // Serialised as BookEvent so the "type" discriminator is written (SignalR would use the runtime type without it).
    private static JsonElement Payload(BookEvent bookEvent) => JsonSerializer.SerializeToElement(bookEvent, JsonSerializerOptions.Web);
}
