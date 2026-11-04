using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.Core;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Auth;

// Account → Agent access (#32 phase 1): JD creates, lists and revokes personal access tokens. The secret is shown once,
// codex:read is always granted, and the /api cookie routes never accept a token. Admins see each user's token count
// and can revoke them all.
[TestFixture]
public class ApiTokenTests : ApiFixture
{
    private Guid _readOnly;
    private string _secret = null!;

    [Test]
    [Order(1)]
    public async Task Create_ShouldReturnTheSecretOnceWithReadOnlyByDefault()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/tokens", new CreateApiTokenRequest(" Claude Code on the desktop ", null, 90));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreatedApiTokenDto>())!;
        _readOnly = created.Token.Id;
        _secret = created.Secret;
        created.Secret.ShouldMatch("^cdx_pat_[0-9A-Za-z]{43}$");
        created.Token.Prefix.ShouldBe(created.Secret[..16]);
        created.Token.Name.ShouldBe("Claude Code on the desktop");
        created.Token.Scopes.ShouldBe([ApiTokenScopes.Read]);
        created.Token.Expired.ShouldBeFalse();
        created.Token.ExpiresLabel.ShouldBe(DisplayDate.ShortDay(created.Token.ExpiresAt!.Value, TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone));
        (created.Token.ExpiresAt.Value - created.Token.CreatedAt).ShouldBe(TimeSpan.FromDays(90));
        created.Token.LastUsedLabel.ShouldBeNull();
        created.Token.CreatedLabel.ShouldBe("just now");
    }

    [Test]
    [Order(2)]
    public async Task Create_WhenScopesAreChosenAndItNeverExpires_ShouldKeepThemInOrderWithRead()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/tokens",
            new CreateApiTokenRequest("Script", [ApiTokenScopes.Generate, ApiTokenScopes.Write], null));

        var created = (await response.Content.ReadFromJsonAsync<CreatedApiTokenDto>())!;
        created.Token.Scopes.ShouldBe([ApiTokenScopes.Read, ApiTokenScopes.Write, ApiTokenScopes.Generate]);
        created.Token.ExpiresAt.ShouldBeNull();
        created.Token.ExpiresLabel.ShouldBeNull();
    }

    [TestCase("", null, 90, "Give the token a name, such as \"Claude Code on the desktop\".")]
    [TestCase("Agent", "codex:admin", 90, "Unknown scope codex:admin: use codex:read, codex:write, codex:references, codex:generate.")]
    [TestCase("Agent", null, 0, "A token can last from 1 to 3650 days, or never expire.")]
    [Order(3)]
    public async Task Create_WhenTheRequestIsInvalid_ShouldBe400(string name, string? scope, int days, string message)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/tokens",
            new CreateApiTokenRequest(name, scope == null ? null : [scope], days));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).ShouldBe(new ApiError(message, ApiErrorCodes.Validation));
    }

    [Test]
    [Order(4)]
    public async Task List_ShouldShowTheTokensNewestFirstWithoutTheirSecrets()
    {
        var response = await Client.GetAsync("/api/auth/tokens");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(_secret);
        var tokens = (await response.Content.ReadFromJsonAsync<List<ApiTokenDto>>())!;
        tokens.Select(t => t.Name).ShouldBe(["Script", "Claude Code on the desktop"]);
    }

    [Test]
    [Order(5)]
    public async Task Api_WhenCalledWithATokenInsteadOfACookie_ShouldBe401()
    {
        using var client = Host.CreateClient().ActingAs(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secret);

        var response = await client.GetAsync("/api/books");

        (await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    [Order(6)]
    public async Task Users_ShouldCountEachUsersWorkingTokens()
    {
        var users = (await Client.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!;

        users.Single(u => u.Email == TestUsers.JdEmail).TokenCount.ShouldBe(2);
    }

    [Test]
    [Order(7)]
    public async Task Revoke_ShouldDropTheTokenFromTheListAndStopIt()
    {
        var response = await Client.DeleteAsync($"/api/auth/tokens/{_readOnly}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var tokens = (await Client.GetFromJsonAsync<List<ApiTokenDto>>("/api/auth/tokens"))!;
        tokens.Select(t => t.Name).ShouldBe(["Script"]);
        (await Host.Services.GetRequiredService<ApiTokens>().AuthenticateAsync(_secret, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    [Order(8)]
    public async Task RevokeTokens_WhenAnAdminRevokesAUsersTokens_ShouldRevokeThemAll()
    {
        var response = await Client.PostAsync($"/api/admin/users/{SeedIds.Users.Jd}/revoke-tokens", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<List<ApiTokenDto>>("/api/auth/tokens"))!.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!.Single(u => u.Email == TestUsers.JdEmail).TokenCount.ShouldBe(0);
    }

    [Test]
    [Order(9)]
    public async Task RevokeTokens_WhenTheUserDoesNotExist_ShouldBe404()
    {
        var response = await Client.PostAsync($"/api/admin/users/{Guid.CreateVersion7()}/revoke-tokens", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task RevokeTokens_WhenTheCallerIsNotAnAdmin_ShouldBe403()
    {
        var userB = await TestUsers.CreateAsync(ConnectionString, "tokens-b@example.test", "User B");
        using var client = Host.CreateClient().ActingAs(userB);

        var response = await client.PostAsync($"/api/admin/users/{SeedIds.Users.Jd}/revoke-tokens", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
