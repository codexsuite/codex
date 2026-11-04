using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Codex.WebApi.Tests.Auth;

// /hubs/book needs a signed-in user, and JoinBook applies the ownership rule.
[TestFixture]
[CancelAfter(20000)]
public class BookHubAuthTests : ApiFixture
{
    private static readonly Guid UserB = new("0199c0de-0000-7000-8000-0000000000b3");

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public Task CreateUserBAsync() => TestUsers.CreateAsync(ConnectionString, "hub-b@example.test", "User B", id: UserB);

    [Test]
    public async Task Connect_WhenAnonymous_ShouldBeRefused()
    {
        await using var connection = Connection(user: null);

        var failure = await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync());

        failure.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task JoinBook_WhenTheBookIsAnotherUsers_ShouldFailWithNotFound()
    {
        await using var connection = Connection(UserB);
        await connection.StartAsync();

        var failure = await Should.ThrowAsync<HubException>(() => connection.InvokeAsync("JoinBook", SeedIds.Books.ProductEngineering));

        var error = JsonSerializer.Deserialize<ApiError>(failure.Message[failure.Message.IndexOf('{', StringComparison.Ordinal)..], JsonSerializerOptions.Web);
        error!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task JoinBook_WhenTheBookIsYourOwn_ShouldJoin()
    {
        await using var connection = Connection(SeedIds.Users.Jd);
        await connection.StartAsync();

        await Should.NotThrowAsync(() => connection.InvokeAsync("JoinBook", SeedIds.Books.ProductEngineering));
    }

    private HubConnection Connection(Guid? user) => new HubConnectionBuilder()
        .WithUrl(Host.BaseUrl + BookHub.Path, options => options.Headers[TestAuthentication.UserHeader] = user?.ToString() ?? TestAuthentication.AnonymousValue)
        .Build();
}
