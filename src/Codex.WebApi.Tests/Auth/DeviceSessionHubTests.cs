using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace Codex.WebApi.Tests.Auth;

// /hubs/* takes a device secret as SignalR's access_token query value (WebSockets cannot send headers).
[TestFixture]
[CancelAfter(20000)]
public class DeviceSessionHubTests : ApiFixture
{
    private const string Email = "hub-device@example.test";
    private const string Password = "a-long-password";

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public Task CreateUserAsync() => TestUsers.CreateAsync(ConnectionString, Email, "Hub Device", Password);

    [Test]
    public async Task Connect_WhenTheAccessTokenIsADeviceSecret_ShouldConnectUntilTheSessionIsRevoked()
    {
        using var anon = AuthClient.Create(Host);
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, Password, "Phone");
        await using var connection = Connection(phone.Secret);

        await Should.NotThrowAsync(() => connection.StartAsync());
        await connection.StopAsync();
        using var device = DeviceClient.WithBearer(Host, phone.Secret);
        (await device.DeleteAsync("/api/auth/device-sessions/current")).EnsureSuccessStatusCode();
        await using var again = Connection(phone.Secret);
        var failure = await Should.ThrowAsync<HttpRequestException>(() => again.StartAsync());
        failure.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    private HubConnection Connection(string secret) => new HubConnectionBuilder()
        .WithUrl(Host.BaseUrl + BookHub.Path, options =>
        {
            options.Headers[TestAuthentication.UserHeader] = TestAuthentication.AnonymousValue;
            options.AccessTokenProvider = () => Task.FromResult<string?>(secret);
        })
        .Build();
}
