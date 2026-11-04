using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Realtime;

// /hubs/book over a real WebSocket against the real app. Each test publishes through a real endpoint (or the bus) and
// reads what each client received. "Receives nothing" is proved without waiting: a later event for another book goes
// through the same relay in order, so once that arrives everything earlier would already have been delivered.
[TestFixture]
[CancelAfter(20000)]
public class BookHubTests : ApiFixture
{
    private Guid _bookA;
    private Guid _sceneA;
    private Guid _bookB;
    private Guid _sceneB;
    private readonly List<Receiver> _receivers = [];

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookA, _, _sceneA) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneA, "A one.");
        (_bookB, _, _sceneB) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneB, "B one.");
    }

    [TearDown]
    public async Task DisconnectAsync()
    {
        foreach (var receiver in _receivers)
        {
            await receiver.DisposeAsync();
        }
        _receivers.Clear();
    }

    [Test]
    public async Task Put_WhenTwoClientsJoinedTheBook_ShouldDeliverProseSavedToBoth()
    {
        var first = await ConnectAsync(_bookA);
        var second = await ConnectAsync(_bookA);

        var version = await SaveAsync(_sceneA, "A two.");

        foreach (var receiver in new[] { first, second })
        {
            var received = await receiver.NextAsync();
            received.GetProperty("type").GetString().ShouldBe("proseSaved");
            received.Deserialize<BookEvent>(JsonSerializerOptions.Web).ShouldBe(new ProseSaved(_bookA, _sceneA, version, "editor"));
        }
    }

    [Test]
    public async Task Put_WhenClientDidNotJoinTheBook_ShouldDeliverNothingForIt()
    {
        var joinedA = await ConnectAsync(_bookA);
        var joinedB = await ConnectAsync(_bookB);

        // Events reach every client in publish order, so a leak would show up as the wrong event arriving first (A)
        // or in between (B).
        await SaveAsync(_sceneB, "B two.");
        await SaveAsync(_sceneA, "A three.");
        await SaveAsync(_sceneB, "B three.");

        (await joinedA.NextAsync()).GetProperty("sceneId").GetGuid().ShouldBe(_sceneA);
        var firstB = (await joinedB.NextAsync()).GetProperty("version").GetInt32();
        var secondB = await joinedB.NextAsync();
        secondB.GetProperty("sceneId").GetGuid().ShouldBe(_sceneB);
        secondB.GetProperty("version").GetInt32().ShouldBe(firstB + 1);
    }

    [Test]
    public async Task LeaveBook_WhenCalled_ShouldStopThatBooksEvents()
    {
        var leaver = await ConnectAsync(_bookA, _bookB);
        await leaver.Connection.InvokeAsync("LeaveBook", _bookA);

        await Bus.PublishAsync(new StructureChanged(_bookA, "scene"));
        await Bus.PublishAsync(new StructureChanged(_bookB, "scene"));

        (await leaver.NextAsync()).GetProperty("bookId").GetGuid().ShouldBe(_bookB);
    }

    [Test]
    public async Task Publish_WhenEachEventKind_ShouldKeepTheWireShape()
    {
        var receiver = await ConnectAsync(_bookA);
        var jobId = Guid.NewGuid();

        await Bus.PublishAsync(new JobProgress(_bookA, jobId, "export", 100, "done", null));
        await Bus.PublishAsync(new BookMetaChanged(_bookA, ["title", "status"]));
        await Bus.PublishAsync(new StructureChanged(_bookA, "chapter"));

        (await receiver.NextAsync()).Deserialize<BookEvent>(JsonSerializerOptions.Web)
            .ShouldBe(new JobProgress(_bookA, jobId, "export", 100, "done", null));
        var meta = await receiver.NextAsync();
        meta.GetProperty("type").GetString().ShouldBe("bookMetaChanged");
        meta.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ShouldBe(["title", "status"]);
        (await receiver.NextAsync()).GetProperty("type").GetString().ShouldBe("structureChanged");
    }

    [Test]
    public async Task Put_WhenRequestNamesItsClient_ShouldCarryTheClientIdOnTheEvent()
    {
        var receiver = await ConnectAsync(_bookA);

        await SaveAsync(_sceneA, "From a tab.", "tab-0199-abc");
        await SaveAsync(_sceneA, "From nowhere.");

        (await receiver.NextAsync()).GetProperty("clientId").GetString().ShouldBe("tab-0199-abc");
        (await receiver.NextAsync()).TryGetProperty("clientId", out _).ShouldBeFalse();
    }

    [TestCase("has spaces")]
    [TestCase("semi;colon")]
    [TestCase("")]
    public async Task Put_WhenClientHeaderIsNotAnId_ShouldLeaveTheClientIdOut(string header)
    {
        var receiver = await ConnectAsync(_bookA);

        await SaveAsync(_sceneA, "Odd header.", header);

        (await receiver.NextAsync()).TryGetProperty("clientId", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Put_WhenClientHeaderIsTooLong_ShouldLeaveTheClientIdOut()
    {
        var receiver = await ConnectAsync(_bookA);

        await SaveAsync(_sceneA, "Long header.", new string('a', 65));
        await SaveAsync(_sceneA, "Longest allowed.", new string('b', 64));

        (await receiver.NextAsync()).TryGetProperty("clientId", out _).ShouldBeFalse();
        (await receiver.NextAsync()).GetProperty("clientId").GetString().ShouldBe(new string('b', 64));
    }

    [Test]
    public async Task JoinBook_WhenBookUnknown_ShouldFailWithNotFoundApiError()
    {
        var receiver = await StartAsync();

        var failure = await Should.ThrowAsync<HubException>(() => receiver.Connection.InvokeAsync("JoinBook", Guid.NewGuid()));

        var error = JsonSerializer.Deserialize<ApiError>(failure.Message[failure.Message.IndexOf('{', StringComparison.Ordinal)..], JsonSerializerOptions.Web);
        error!.Code.ShouldBe(ApiErrorCodes.NotFound);
        error.Error.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task JoinBook_WhenIdIsASeries_ShouldFailWithNotFoundApiError()
    {
        await using var db = await OpenDatabaseAsync();
        var seriesId = await TestRows.SeriesAsync(db);
        var receiver = await StartAsync();

        var failure = await Should.ThrowAsync<HubException>(() => receiver.Connection.InvokeAsync("JoinBook", seriesId));

        failure.Message.ShouldContain($"\"code\":\"{ApiErrorCodes.NotFound}\"");
    }

    // Saves through the endpoint at the scene's current version, as the browser tab `clientId` (none when null);
    // returns the version it was saved as.
    private async Task<int> SaveAsync(Guid sceneId, string body, string? clientId = null)
    {
        var current = await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose", JsonSerializerOptions.Web);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/scenes/{sceneId}/prose")
        {
            Content = JsonContent.Create(new SaveProseRequest(body, current!.Version)),
        };
        if (clientId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Codex-Client", clientId);
        }
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version;
    }

    private IBookEvents Bus => Host.Services.GetRequiredService<IBookEvents>();

    private async Task<Receiver> StartAsync()
    {
        var receiver = new Receiver(Host.BaseUrl + BookHub.Path);
        _receivers.Add(receiver);
        await receiver.Connection.StartAsync();
        return receiver;
    }

    private async Task<Receiver> ConnectAsync(params Guid[] books)
    {
        var receiver = await StartAsync();
        foreach (var book in books)
        {
            await receiver.Connection.InvokeAsync("JoinBook", book);
        }
        return receiver;
    }

    private sealed class Receiver : IAsyncDisposable
    {
        private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();

        public Receiver(string url)
        {
            Connection = new HubConnectionBuilder().WithUrl(url).Build();
            Connection.On<JsonElement>(BookHub.EventMethod, e => _events.Writer.TryWrite(e));
        }

        public HubConnection Connection { get; }

        public async Task<JsonElement> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await _events.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
