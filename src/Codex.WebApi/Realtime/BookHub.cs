using System.Text.Json;
using Codex.Application.Features.Shell;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Codex.WebApi.Realtime;

// /hubs/book: the realtime transport for IBookEvents (events only; no co-editing in v1). Signed-in users only (the
// cookie travels on the same-origin WebSocket), and JoinBook applies the access rule (#80): it needs view on the book,
// checked by the book header query (IBookAccess.RequireAsync with BookRole.View, then the book itself, so a series id
// is refused too). A book the caller cannot open is not_found, the same as one that does not exist.
//
// A client calls JoinBook(bookId) and from then on receives that book's events as "event" messages whose payload is
// the BookEvent JSON (the same "type"-discriminated shape as Codex.Contracts.Events and src/Codex.Core.Client/src/api/events.ts).
// It receives nothing for a book it has not joined. LeaveBook(bookId) stops that book's events; a closed connection
// leaves every book. Joining a book twice is harmless. AccessChanged events reach every connection of the user they
// name, joined or not; when one says the user lost the book, the relay removes their connections from its group.
//
// An unknown book fails JoinBook with a HubException whose message is an ApiError body, {"error":"...","code":"not_found"},
// so a client reads it with the same code vocabulary as REST. The SignalR client wraps the message in its own sentence;
// the JSON object is the part from the first '{'.
[Authorize]
public sealed class BookHub(IShellQueries books, BookGroupMembers members) : Hub
{
    public const string Path = "/hubs/book";

    public const string EventMethod = "event";

    public static string GroupName(Guid bookId) => $"book:{bookId}";

    public async Task JoinBook(Guid bookId)
    {
        try
        {
            await books.GetBookHeaderAsync(bookId, Context.ConnectionAborted);
        }
        catch (ApiException e)
        {
            throw new HubException(JsonSerializer.Serialize(e.ToApiError(), JsonSerializerOptions.Web));
        }
        members.Joined(Context.ConnectionId, Guid.Parse(Context.UserIdentifier!), bookId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bookId));
    }

    public Task LeaveBook(Guid bookId)
    {
        members.Left(Context.ConnectionId, bookId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        members.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
