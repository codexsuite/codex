using System.Text.Json;
using Codex.Application.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Events;

// The receiving half of the cross-process bridge, in the processes whose BookEventsSettings.Listen is on (the web
// host): LISTENs on BookEventsNotifier.Channel and hands each event another process published to this process's
// subscribers (the SignalR relay, the quote-flag scanner), skipping the ones this process sent itself, which its
// subscribers already had.
//   - One dedicated, unpooled connection (application_name ApplicationName), opened from the app's connection string,
//     not a pooled one held forever. The first LISTEN is made while the host starts, so nothing published once the host
//     is up is missed.
//   - Any failure (a dropped connection, a refused connect, anything else thrown) closes the connection and reopens it
//     with back-off, FirstRetry doubling to MaxRetry. The back-off and the outage only end once a connection has
//     proved itself (a notification arrived on it, or it stayed up StableAfter), so a connection that opens and drops
//     again keeps backing off. The first failure of an outage is logged as a warning, the repeats at Debug; events sent
//     while it was down are not replayed.
//   - A notification that is not a book event (bad JSON, unknown type) is logged and skipped; the next one is delivered.
public sealed class BookEventsListener(
    InMemoryBookEvents bus,
    BookEventsNotifier notifier,
    BookEventsSettings settings,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<BookEventsListener> logger) : IHostedService
{
    public const string ApplicationName = "codex-events-listener";
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(60);

    // NOTIFY payloads are short; a keepalive notices a silently dead connection while it waits.
    private const int KeepAliveSeconds = 30;

    private CancellationTokenSource? _stop;
    private Task? _loop;
    private bool _failing;
    private bool _heard;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!settings.Listen)
        {
            return;
        }
        var stop = new CancellationTokenSource();
        _stop = stop;
        NpgsqlConnection? connection = null;
        try
        {
            connection = await ListenAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Failed(exception, "could not connect");
        }
        _loop = Task.Run(() => RunAsync(connection, stop.Token), CancellationToken.None);
    }

    // The host may stop it twice (stop, then dispose); only the first does anything.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stop = Interlocked.Exchange(ref _stop, null);
        if (stop == null)
        {
            return;
        }
        await stop.CancelAsync();
        if (_loop != null)
        {
            // The loop ends as soon as it sees the cancellation (it holds no request open).
            await _loop;
        }
        stop.Dispose();
    }

    private async Task RunAsync(NpgsqlConnection? connection, CancellationToken stop)
    {
        var retry = FirstRetry;
        var connectedAt = clock.GetTimestamp();
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (connection == null)
                {
                    await Task.Delay(retry, clock, stop);
                    retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
                    connection = await ListenAsync(stop);
                    connectedAt = clock.GetTimestamp();
                    continue;
                }
                while (!stop.IsCancellationRequested)
                {
                    await connection.WaitAsync(stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Host stopping.
            }
            catch (Exception exception)
            {
                if (connection != null)
                {
                    if (_heard || clock.GetElapsedTime(connectedAt) >= StableAfter)
                    {
                        // The connection had proved itself: this is a new outage.
                        retry = FirstRetry;
                        _failing = false;
                    }
                    await connection.DisposeAsync();
                    connection = null;
                    Failed(exception, "lost its connection");
                }
                else
                {
                    Failed(exception, "could not connect");
                }
            }
        }
        if (connection != null)
        {
            await connection.DisposeAsync();
        }
    }

    // Opens a dedicated connection and LISTENs on it; on any failure the connection is closed and the exception thrown.
    private async Task<NpgsqlConnection> ListenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(ConnectionString());
        connection.Notification += OnNotification;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(BookEventsNotifier.ListenCommand, cancellationToken: cancellationToken));
        }
        catch (Exception)
        {
            await connection.DisposeAsync();
            throw;
        }
        _heard = false;
        logger.LogInformation("Listening for book events from other processes on {Channel}", BookEventsNotifier.Channel);
        return connection;
    }

    private void Failed(Exception exception, string what)
    {
        if (_failing)
        {
            logger.LogDebug(exception, "Book events listener {What} again; retrying", what);
            return;
        }
        _failing = true;
        logger.LogWarning(exception, "Book events listener {What}; retrying with back-off until it is listening again", what);
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        _heard = true;
        BookEventEnvelope? envelope;
        try
        {
            envelope = BookEventsNotifier.Deserialize(args.Payload);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "Ignored a notification on {Channel} that is not a book event", args.Channel);
            return;
        }
        if (envelope?.Event == null || envelope.Origin == notifier.InstanceId)
        {
            return;
        }
        bus.DeliverFromElsewhere(envelope.Event);
    }

    private string ConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            configuration[DatabaseServices.ConnectionKey] ?? throw new InvalidOperationException($"{DatabaseServices.ConnectionKey} is not set."))
        {
            Pooling = false,
            Timezone = "UTC",
            ApplicationName = ApplicationName,
            KeepAlive = KeepAliveSeconds,
        };
        return builder.ConnectionString;
    }
}
