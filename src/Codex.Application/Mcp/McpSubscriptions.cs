using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Resource subscriptions (#20 Part 5): the subscriptions/listen handler of protocol 2026-07-28. It replaces the SDK's own
// handler, so it acknowledges once (only the resource URIs it honours), then holds the request open and sends
// notifications/resources/updated, tagged with the listen request's id, for each IBookEvents event that changes a
// subscribed resource. It ends, and unsubscribes, when the request is cancelled (stdio notifications/cancelled, HTTP
// disconnect). Only the six codex:// resources can be subscribed to, and only the caller's own: anything else is
// not_found, the same as reading it. The listen request's response stream carries the notifications, so this works on
// the stateless HTTP transport as well as stdio.
// Events reach this process from its own writes always, and from other processes when it listens to the database
// (the web host does; the stdio process does not, docs/events.md), so a stdio session hears only its own writes.
public static class McpSubscriptions
{
    public static async ValueTask<EmptyResult> ListenAsync(RequestContext<SubscriptionsListenRequestParams> request, CancellationToken cancellationToken)
    {
        var services = request.Services ?? throw new InvalidOperationException("No services on a subscriptions/listen request.");
        var requested = request.Params?.Notifications?.ResourceSubscriptions ?? [];
        IReadOnlyList<Watched> watched;
        try
        {
            // Like reading a resource: every token holds codex:read, so this only checks a stdio token still works.
            await services.GetRequiredService<McpAccess>().CheckAsync(cancellationToken);
            watched = await ResolveAsync(services, requested, cancellationToken);
        }
        catch (McpToolException e)
        {
            throw new McpException($"{e.Code}: {e.Message}", e);
        }

        var meta = new JsonObject { [MetaKeys.SubscriptionId] = request.JsonRpcRequest.Id.ToString() };
        var acknowledged = new SubscriptionsListenNotifications { ResourceSubscriptions = watched.Select(w => w.Uri).ToList() };
        await Send(request.Server, NotificationMethods.SubscriptionsAcknowledgedNotification,
            new SubscriptionsAcknowledgedNotificationParams { Notifications = acknowledged }, meta, cancellationToken);

        var updates = System.Threading.Channels.Channel.CreateUnbounded<string>();
        using var subscription = services.GetRequiredService<IBookEvents>().Subscribe(null, bookEvent =>
        {
            foreach (var uri in watched.Where(w => w.IsChangedBy(bookEvent)).Select(w => w.Uri))
            {
                updates.Writer.TryWrite(uri);
            }
            return Task.CompletedTask;
        });
        try
        {
            await foreach (var uri in updates.Reader.ReadAllAsync(cancellationToken))
            {
                await Send(request.Server, NotificationMethods.ResourceUpdatedNotification,
                    new ResourceUpdatedNotificationParams { Uri = uri }, meta, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client ended the subscription.
        }
        return new EmptyResult();
    }

    private static async Task<IReadOnlyList<Watched>> ResolveAsync(IServiceProvider services, IEnumerable<string> uris, CancellationToken cancellationToken)
    {
        var refs = services.GetRequiredService<McpRefs>();
        var queries = services.GetRequiredService<McpQueries>();
        var books = await refs.AccessibleAllAsync(cancellationToken);
        var watched = new List<Watched>();
        foreach (var uri in uris.Distinct())
        {
            watched.Add(await WatchAsync(uri, books, queries, cancellationToken));
        }
        return watched;
    }

    private static async Task<Watched> WatchAsync(string uri, IReadOnlyDictionary<Guid, McpBook> books, McpQueries queries, CancellationToken cancellationToken)
    {
        var parts = uri.StartsWith("codex://", StringComparison.Ordinal) ? uri["codex://".Length..].Split('/') : [];
        switch (parts)
        {
            case ["books"]:
                return new Watched(uri, books.Keys.ToHashSet(), e => e is BookMetaChanged);
            case ["book", var id, "outline"]:
                return new Watched(uri, OpenableBook(uri, id, books), e => e is ProseSaved or StructureChanged or BookMetaChanged);
            case ["book", var id, "style-guide"]:
                return new Watched(uri, OpenableBook(uri, id, books), e => e is BookMetaChanged);
            case ["book", var id, "references"]:
                return new Watched(uri, OpenableBook(uri, id, books), e => e is ProseSaved or StructureChanged);
            case ["scene", var id] when Guid.TryParse(id, out var sceneId):
                var bookId = await queries.BookOfAsync(sceneId, null, cancellationToken) ?? throw McpToolException.NotFound($"No scene {id}.");
                return new Watched(uri, [bookId], e => e is ProseSaved saved && saved.SceneId == sceneId);
            case ["kb", var id] when Guid.TryParse(id, out _):
                // A Knowledge base entry's events name its owner (a book or a series), not the entry: any kb change in the
                // caller's Library is announced and the client re-reads.
                return new Watched(uri, books.Keys.ToHashSet(), e => e is StructureChanged { Kind: "kb" });
            default:
                throw McpToolException.NotFound($"{uri} is not a resource that can be subscribed to.");
        }
    }

    private static HashSet<Guid> OpenableBook(string uri, string id, IReadOnlyDictionary<Guid, McpBook> books) =>
        Guid.TryParse(id, out var bookId) && books.ContainsKey(bookId)
            ? [bookId]
            : throw McpToolException.NotFound($"No book {id} (subscribing to {uri}).");

    private static Task Send<T>(McpServer server, string method, T parameters, JsonObject meta, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions) as JsonObject ?? new JsonObject();
        body["_meta"] = meta.DeepClone();
        return server.SendMessageAsync(new JsonRpcNotification { Method = method, Params = body }, cancellationToken);
    }

    // A subscribed URI, the books (or series) whose events can change it, and which of their events do.
    private sealed record Watched(string Uri, HashSet<Guid> Books, Func<BookEvent, bool> Changes)
    {
        public bool IsChangedBy(BookEvent bookEvent) => Books.Contains(bookEvent.BookId) && Changes(bookEvent);
    }
}
