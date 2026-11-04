using System.Net.Http.Json;
using System.Text.Json;
using System.Transactions;
using Codex.Application.Events;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Events;

// Two processes on one database: the fixture's host is the web host (listening, as by default), and a second host on
// the same database with CODEX_EVENTS_LISTEN off stands in for the stdio MCP server or the worker. What the second one
// publishes reaches the web host's bus subscribers and SignalR clients once, through Postgres NOTIFY.
// "Nothing else arrived" is proved without waiting: NOTIFYs reach the listener in commit order, so a marker the other
// process sends afterwards would arrive after anything wrongly delivered first.
[TestFixture]
[CancelAfter(30000)]
public class BookEventsBridgeTests : ApiFixture
{
    private readonly CapturedLogs _webLogs = new();
    private readonly CapturedLogs _otherLogs = new();
    private CodexHost _other = null!;
    private HttpClient _otherClient = null!;
    private Guid _bookId;
    private Guid _sceneId;

    protected override Action<IServiceCollection>? ConfigureTestServices => _webLogs.ReplaceLoggingIn;

    private IBookEvents WebBus => Host.Services.GetRequiredService<IBookEvents>();

    private IBookEvents OtherBus => _other.Services.GetRequiredService<IBookEvents>();

    [OneTimeSetUp]
    public async Task StartOtherProcessAsync()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
            await TestRows.ProseAsync(db, _sceneId, "One.");
        }
        _other = CodexHost.Start(ConnectionString, services =>
        {
            services.AddSingleton(new BookEventsSettings(Listen: false));
            _otherLogs.ReplaceLoggingIn(services);
        });
        _otherClient = _other.CreateClient();
    }

    [OneTimeTearDown]
    public async Task StopOtherProcessAsync()
    {
        _otherClient?.Dispose();
        if (_other != null)
        {
            await _other.DisposeAsync();
        }
    }

    [Test, Order(1)]
    public async Task Put_WhenAnotherProcessSavesProse_ShouldReachThisHostsSubscribersAndClientsOnce()
    {
        using var web = new EventCollector(WebBus, _bookId);
        using var other = new EventCollector(OtherBus, _bookId);
        await using var tab = await HubReceiver.JoinAsync(Host.BaseUrl, _bookId);
        var marker = new StructureChanged(_bookId, "scene");

        var version = await SaveAsync(_otherClient, "Two, from elsewhere.", "tab-elsewhere");
        await OtherBus.PublishAsync(marker);

        var saved = new ProseSaved(_bookId, _sceneId, version, "editor") { ClientId = "tab-elsewhere" };
        (await web.NextAsync()).ShouldBe(saved);
        (await web.NextAsync()).ShouldBe(marker);
        (await tab.NextAsync()).Deserialize<BookEvent>(JsonSerializerOptions.Web).ShouldBe(saved);
        (await tab.NextAsync()).GetProperty("type").GetString().ShouldBe("structureChanged");
        (await other.NextAsync()).ShouldBe(saved);
        (await other.NextAsync()).ShouldBe(marker);
    }

    [Test, Order(2)]
    public async Task Put_WhenThisHostSavesProse_ShouldDeliverItsOwnEventOnceAndNotToANonListener()
    {
        using var web = new EventCollector(WebBus, _bookId);
        using var other = new EventCollector(OtherBus, _bookId);
        var marker = new StructureChanged(_bookId, "chapter");

        var version = await SaveAsync(Client, "Three, from here.");
        await OtherBus.PublishAsync(marker);

        (await web.NextAsync()).ShouldBe(new ProseSaved(_bookId, _sceneId, version, "editor"));
        (await web.NextAsync()).ShouldBe(marker);
        (await other.NextAsync()).ShouldBe(marker);
    }

    [Test, Order(3)]
    public async Task Publish_WhenTheEventIsOverTheNotifyLimit_ShouldDeliverItLocallyOnlyAndLogIt()
    {
        using var web = new EventCollector(WebBus, _bookId);
        using var other = new EventCollector(OtherBus, _bookId);
        var oversize = MetaChangedOfPayloadSize(BookEventsNotifier.MaxPayloadBytes + 1);
        var marker = new StructureChanged(_bookId, "label");

        await OtherBus.PublishAsync(oversize);
        await OtherBus.PublishAsync(marker);

        (await web.NextAsync()).ShouldBe(marker);
        (await other.NextAsync()).ShouldBe(oversize);
        (await other.NextAsync()).ShouldBe(marker);
        var warning = _otherLogs.Matching(l => l.Category == typeof(BookEventsNotifier).FullName).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain($"{BookEventsNotifier.MaxPayloadBytes + 1} bytes");
    }

    [Test, Order(4)]
    public async Task Publish_WhenTheEventIsExactlyAtTheNotifyLimit_ShouldReachThisHost()
    {
        using var web = new EventCollector(WebBus, _bookId);
        var largest = MetaChangedOfPayloadSize(BookEventsNotifier.MaxPayloadBytes);

        await OtherBus.PublishAsync(largest);

        (await web.NextAsync()).ShouldBeOfType<BookMetaChanged>().Fields.ShouldBe(largest.Fields);
    }

    [Test, Order(5)]
    public async Task Publish_WhenInsideARolledBackTransactionScope_ShouldNotReachThisHost()
    {
        using var web = new EventCollector(WebBus, _bookId);
        using var other = new EventCollector(OtherBus, _bookId);
        var rolledBack = new StructureChanged(_bookId, "beat");
        var marker = new StructureChanged(_bookId, "kb");

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await OtherBus.PublishAsync(rolledBack);
        }
        await OtherBus.PublishAsync(marker);

        (await web.NextAsync()).ShouldBe(marker);
        (await other.NextAsync()).ShouldBe(rolledBack);
    }

    [Test, Order(6)]
    public async Task Publish_WhenInsideACompletedTransactionScope_ShouldReachThisHost()
    {
        using var web = new EventCollector(WebBus, _bookId);
        var committed = new StructureChanged(_bookId, "beat");

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await OtherBus.PublishAsync(committed);
            scope.Complete();
        }

        (await web.NextAsync()).ShouldBe(committed);
    }

    [Test, Order(7)]
    public async Task Notify_WhenPayloadsAreNotBookEvents_ShouldSkipThemAndKeepDelivering()
    {
        using var web = new EventCollector(WebBus, _bookId);
        var forger = Guid.NewGuid();
        var marker = new StructureChanged(_bookId, "scene");

        await NotifyAsync("not json");
        await NotifyAsync($$$"""{"origin":"{{{forger}}}","event":{"type":"nope","bookId":"{{{_bookId}}}"}}""");
        await NotifyAsync($$$"""{"origin":"{{{forger}}}","event":{"type":"structureChanged","bookId":"{{{_bookId}}}","kind":"act","clientId":"has spaces"}}""");
        await NotifyAsync(BookEventsNotifier.Serialize(new BookEventEnvelope(forger, marker)));

        (await web.NextAsync()).ShouldBe(new StructureChanged(_bookId, "act"));
        (await web.NextAsync()).ShouldBe(marker);
        _webLogs.Matching(l => l.Category == typeof(BookEventsListener).FullName && l.Message.Contains("not a book event"))
            .Count.ShouldBe(2);
        _webLogs.Matching(Listening).Count.ShouldBe(1);
    }

    // Last: it drops the web host's LISTEN connection, twice. The second drop comes before the new connection has proved
    // itself, so it is the same outage: no second warning, and the retry backs off further.
    [Test, Order(8)]
    public async Task Listen_WhenItsConnectionIsTerminated_ShouldReconnectWarnOnceAndKeepDelivering()
    {
        using var web = new EventCollector(WebBus, _bookId);
        _webLogs.Matching(Listening).Count.ShouldBe(1);

        await TerminateListenerAsync();
        await _webLogs.WaitForAsync(Listening, count: 2);
        await TerminateListenerAsync();
        await _webLogs.WaitForAsync(Listening, count: 3);
        var afterwards = new StructureChanged(_bookId, "act");
        await OtherBus.PublishAsync(afterwards);

        (await web.NextAsync()).ShouldBe(afterwards);
        _webLogs.Matching(l => l.Category == typeof(BookEventsListener).FullName && l.Message.Contains("lost its connection"))
            .Select(l => l.Level).ShouldBe([LogLevel.Warning, LogLevel.Debug]);
    }

    private static bool Listening(CapturedLog l) => l.Category == typeof(BookEventsListener).FullName && l.Level == LogLevel.Information;

    private async Task TerminateListenerAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM (
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                WHERE datname = current_database() AND application_name = @ApplicationName) AS terminated
            """,
            new { BookEventsListener.ApplicationName })).ShouldBe(1);
    }

    private async Task NotifyAsync(string payload)
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("SELECT pg_notify(@Channel, @payload)", new { BookEventsNotifier.Channel, payload });
    }

    // A BookMetaChanged whose NOTIFY payload, as the other process sends it, is exactly `bytes` long.
    private BookMetaChanged MetaChangedOfPayloadSize(int bytes)
    {
        var origin = _other.Services.GetRequiredService<BookEventsNotifier>().InstanceId;
        var empty = new BookMetaChanged(_bookId, [string.Empty]);
        var emptySize = BookEventsNotifier.Serialize(new BookEventEnvelope(origin, empty)).Length;
        return new BookMetaChanged(_bookId, [new string('x', bytes - emptySize)]);
    }

    // Saves through the endpoint at the scene's current version, as the browser tab `clientId` (none when null);
    // returns the version it was saved as.
    private async Task<int> SaveAsync(HttpClient client, string body, string? clientId = null)
    {
        var current = await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/scenes/{_sceneId}/prose")
        {
            Content = JsonContent.Create(new SaveProseRequest(body, current!.Version)),
        };
        if (clientId is not null)
        {
            request.Headers.TryAddWithoutValidation(InMemoryBookEvents.ClientHeader, clientId);
        }
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version;
    }
}
