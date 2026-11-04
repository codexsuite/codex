using System.Text;
using System.Text.Json;
using System.Transactions;
using Codex.Contracts.Events;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Events;

// The sending half of the cross-process bridge: InMemoryBookEvents hands every published event here, which NOTIFYs it
// on Channel so BookEventsListener in the other processes on the same database (the web host, from the stdio MCP
// server or the worker) can republish it to their own subscribers. NOTIFY is scoped to the database, so processes on
// different databases never hear each other.
//   - The connection comes from the app's NpgsqlDataSource, so inside an ambient System.Transactions scope it enlists
//     and the notification goes out only if that transaction commits; otherwise it is sent on its own at once.
//   - A payload over MaxPayloadBytes (NOTIFY refuses 8000 bytes or more) is logged and not sent. Getting a connection
//     and sending is bounded by SendTimeout, so a struggling database delays a save by at most that; a timeout, a
//     database failure, or a disposed data source or aborted scope at shutdown is logged. None reaches the publisher:
//     its write has already happened and its own process has the event.
//   - One NOTIFY per event: a save of n scenes sends n, one after another.
public sealed class BookEventsNotifier(NpgsqlDataSource dataSource, ILogger<BookEventsNotifier> logger)
{
    public const string Channel = "codex_book_events";
    public const string ListenCommand = "LISTEN " + Channel;
    public const int MaxPayloadBytes = 7999;
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);

    // This process's id on the wire, so its listener can skip what it sent itself.
    public Guid InstanceId { get; } = Guid.NewGuid();

    public static string Serialize(BookEventEnvelope envelope) => JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web);

    public static BookEventEnvelope? Deserialize(string payload)
        => JsonSerializer.Deserialize<BookEventEnvelope>(payload, JsonSerializerOptions.Web);

    public async Task NotifyAsync(BookEvent bookEvent)
    {
        var payload = Serialize(new BookEventEnvelope(InstanceId, bookEvent));
        var bytes = Encoding.UTF8.GetByteCount(payload);
        if (bytes > MaxPayloadBytes)
        {
            logger.LogWarning(
                "Book event {EventType} for book {BookId} is {Bytes} bytes, over the {MaxBytes}-byte NOTIFY limit; other processes will not see it",
                bookEvent.GetType().Name, bookEvent.BookId, bytes, MaxPayloadBytes);
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(SendTimeout);
            await using var connection = await dataSource.OpenConnectionAsync(timeout.Token);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_notify(@channel, @payload)", new { channel = Channel, payload }, cancellationToken: timeout.Token));
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException or ObjectDisposedException
                                              or TransactionException)
        {
            logger.LogError(exception, "Could not send book event {EventType} for book {BookId} to other processes",
                bookEvent.GetType().Name, bookEvent.BookId);
        }
    }
}
