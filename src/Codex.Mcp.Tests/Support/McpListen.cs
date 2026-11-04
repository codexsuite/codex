using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Codex.Mcp.Tests.Support;

// A subscriptions/listen request (protocol 2026-07-28) held open. SDK 2.2.0's client has no helper for it
// (SubscribeToResourceAsync still sends the retired resources/subscribe), so this sends the request itself: it is answered
// only when cancelled, the acknowledgement and each resources/updated arrive as notifications. Disposing cancels it.
public sealed class McpListen : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(McpCalls.Token);
    private readonly Channel<string> _updates = Channel.CreateUnbounded<string>();
    private readonly List<IAsyncDisposable> _handlers = [];

    private McpListen()
    {
    }

    // The acknowledgement's params: which of the requested resources the server honours.
    public JsonNode Acknowledged { get; private set; } = new JsonObject();

    // Throws McpException when the server refuses the request before acknowledging it.
    public static async Task<McpListen> StartAsync(McpClient client, params string[] uris)
    {
        var listen = new McpListen();
        var id = Guid.NewGuid().ToString();
        var acknowledged = Channel.CreateUnbounded<JsonNode>();
        listen._handlers.Add(client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification, (notification, _) =>
        {
            // Notifications of other listen requests share the stdio channel: keep this request's own.
            if (notification.Params?["_meta"]?[MetaKeys.SubscriptionId]?.ToString() == id)
            {
                listen._updates.Writer.TryWrite(notification.Params["uri"]!.GetValue<string>());
            }
            return ValueTask.CompletedTask;
        }));
        listen._handlers.Add(client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification, (notification, _) =>
        {
            acknowledged.Writer.TryWrite(notification.Params!);
            return ValueTask.CompletedTask;
        }));
        var request = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Id = new RequestId(id),
            Params = new JsonObject
            {
                ["notifications"] = new JsonObject { ["resourceSubscriptions"] = new JsonArray(uris.Select(u => (JsonNode)u).ToArray()) },
            },
        };
        var response = client.SendRequestAsync(request, listen._stop.Token);
        var ack = acknowledged.Reader.ReadAsync(McpCalls.Token).AsTask();
        try
        {
            // Whichever comes first: the acknowledgement, or the request failing (a refused subscription).
            if (await Task.WhenAny(ack, response) == response)
            {
                await response;
            }
            listen.Acknowledged = await ack;
            return listen;
        }
        catch
        {
            await listen.DisposeAsync();
            throw;
        }
    }

    // The next resources/updated URI.
    public async Task<string> NextAsync() => await _updates.Reader.ReadAsync(McpCalls.Token);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        foreach (var handler in _handlers)
        {
            await handler.DisposeAsync();
        }
        _stop.Dispose();
    }
}
