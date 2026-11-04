using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Realtime;

// /hubs/book under the access rule (#80), on the seed: Ana views Product Engineering and edits the Forsaken Gods series.
// JoinBook needs view; AccessChanged goes only to the user it names, and a revoke takes their connections out of the
// book's group. "Receives nothing" is proved as in BookHubTests: a later event through the same relay arrives first.
[TestFixture]
[CancelAfter(20000)]
public class BookHubSharingTests : ApiFixture
{
    private static readonly Guid Stranger = new("0199c0de-0000-7000-8000-0000000000d4");
    private static readonly Guid Pe = SeedIds.Books.ProductEngineering;
    private static readonly Guid Guide = SeedIds.Books.PlayersGuide;
    private readonly List<Receiver> _receivers = [];

    [OneTimeSetUp]
    public Task AddStrangerAsync() => TestUsers.CreateAsync(ConnectionString, "hub-stranger@example.test", "Stranger", id: Stranger);

    [TearDown]
    public async Task DisconnectAsync()
    {
        foreach (var receiver in _receivers)
        {
            await receiver.DisposeAsync();
        }
        _receivers.Clear();
    }

    private IBookEvents Bus => Host.Services.GetRequiredService<IBookEvents>();

    [Test]
    [Order(1)]
    public async Task JoinBook_WhenTheBookIsSharedToView_ShouldJoinAndReceiveItsEvents()
    {
        var ana = await ConnectAsync(SeedIds.Users.Ana, Pe);

        await Bus.PublishAsync(new StructureChanged(Pe, "scene"));

        (await ana.NextAsync()).GetProperty("bookId").GetGuid().ShouldBe(Pe);
    }

    [Test]
    [Order(2)]
    public async Task JoinBook_WhenTheBookIsNotSharedWithTheCaller_ShouldFailWithNotFound()
    {
        var stranger = await StartAsync(Stranger);

        var failure = await Should.ThrowAsync<HubException>(() => stranger.Connection.InvokeAsync("JoinBook", Pe));

        failure.Message.ShouldContain($"\"code\":\"{ApiErrorCodes.NotFound}\"");
    }

    [Test]
    [Order(3)]
    public async Task PutGrant_WhenTheRoleChanges_ShouldTellOnlyThatUser()
    {
        var ana = await ConnectAsync(SeedIds.Users.Ana, Pe);
        var jd = await ConnectAsync(SeedIds.Users.Jd, Pe);

        (await Client.PutAsJsonAsync($"/api/books/{Pe}/sharing/{SeedIds.Users.Ana}", new SetGrantRequest("comment"))).EnsureSuccessStatusCode();
        await Bus.PublishAsync(new StructureChanged(Pe, "chapter"));

        (await ana.NextAsync()).Deserialize<BookEvent>(JsonSerializerOptions.Web).ShouldBe(new AccessChanged(Pe, SeedIds.Users.Ana, "comment"));
        (await ana.NextAsync()).GetProperty("type").GetString().ShouldBe("structureChanged");
        (await jd.NextAsync()).GetProperty("type").GetString().ShouldBe("structureChanged");
    }

    [Test]
    [Order(4)]
    public async Task DeleteGrant_WhenAccessEnds_ShouldTellTheUserAndDropThemFromTheGroup()
    {
        var ana = await ConnectAsync(SeedIds.Users.Ana, Pe, Guide);

        (await Client.DeleteAsync($"/api/books/{Pe}/sharing/{SeedIds.Users.Ana}")).EnsureSuccessStatusCode();
        await Bus.PublishAsync(new StructureChanged(Pe, "scene"));
        await Bus.PublishAsync(new StructureChanged(Guide, "scene"));

        (await ana.NextAsync()).Deserialize<BookEvent>(JsonSerializerOptions.Web).ShouldBe(new AccessChanged(Pe, SeedIds.Users.Ana, null));
        (await ana.NextAsync()).GetProperty("bookId").GetGuid().ShouldBe(Guide);
    }

    private async Task<Receiver> StartAsync(Guid user)
    {
        var receiver = new Receiver(Host.BaseUrl + BookHub.Path, user);
        _receivers.Add(receiver);
        await receiver.Connection.StartAsync();
        return receiver;
    }

    private async Task<Receiver> ConnectAsync(Guid user, params Guid[] books)
    {
        var receiver = await StartAsync(user);
        foreach (var book in books)
        {
            await receiver.Connection.InvokeAsync("JoinBook", book);
        }
        return receiver;
    }

    private sealed class Receiver : IAsyncDisposable
    {
        private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();

        public Receiver(string url, Guid user)
        {
            Connection = new HubConnectionBuilder()
                .WithUrl(url, options => options.Headers[TestAuthentication.UserHeader] = user.ToString())
                .Build();
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
