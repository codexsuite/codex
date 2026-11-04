using System.Net;
using Codex.Application.Identity;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Codex.WebApi.Tests.Auth;

// The production first-admin path: CODEX_ADMIN_EMAIL / CODEX_ADMIN_PASSWORD set a password only where there is none.
[TestFixture]
public class AdminBootstrapTests : ApiFixture
{
    protected override TestUser SignedInAs => TestUser.Anonymous;

    [Test]
    [Order(1)]
    public async Task Start_WhenTheUserHasNoPassword_ShouldSetItAndMakeThemAdmin()
    {
        await TestUsers.CreateAsync(ConnectionString, "owner@example.test", "Owner");

        await BootstrapAsync("Owner@Example.test", "bootstrap-password");

        using var client = AuthClient.Create(Host);
        (await client.LoginAsync("owner@example.test", "bootstrap-password")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(2)]
    public async Task Start_WhenTheUserAlreadyHasAPassword_ShouldLeaveItAlone()
    {
        await BootstrapAsync(TestUsers.JdEmail, "another-password");

        using var client = AuthClient.Create(Host);
        (await client.LoginAsync(TestUsers.JdEmail, "another-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.LoginAsync(TestUsers.JdEmail, TestUsers.DevPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(3)]
    public async Task Start_WhenNoUserHasTheEmail_ShouldCreateTheAdmin()
    {
        await BootstrapAsync("first@example.test", "first-admin-password");

        using var client = AuthClient.Create(Host);
        (await client.LoginAsync("first@example.test", "first-admin-password")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task BootstrapAsync(string email, string password)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AdminBootstrap.EmailKey] = email,
            [AdminBootstrap.PasswordKey] = password,
        }).Build();
        return new AdminBootstrap(Host.Services.GetRequiredService<NpgsqlDataSource>(), new Passwords(), configuration, NullLogger<AdminBootstrap>.Instance)
            .StartAsync(CancellationToken.None);
    }
}
