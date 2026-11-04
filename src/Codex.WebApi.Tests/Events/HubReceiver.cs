using System.Text.Json;
using System.Threading.Channels;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace Codex.WebApi.Tests.Events;

// A SignalR client on one host's /hubs/book that joined one book, keeping what it receives in order.
public sealed class HubReceiver : IAsyncDisposable
{
    private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
    private readonly HubConnection _connection;

    private HubReceiver(string url)
    {
        _connection = new HubConnectionBuilder().WithUrl(url).Build();
        _connection.On<JsonElement>(BookHub.EventMethod, e => _events.Writer.TryWrite(e));
    }

    public static async Task<HubReceiver> JoinAsync(string baseUrl, Guid bookId)
    {
        var receiver = new HubReceiver(baseUrl + BookHub.Path);
        await receiver._connection.StartAsync();
        await receiver._connection.InvokeAsync("JoinBook", bookId);
        return receiver;
    }

    public async Task<JsonElement> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _events.Reader.ReadAsync(timeout.Token);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
