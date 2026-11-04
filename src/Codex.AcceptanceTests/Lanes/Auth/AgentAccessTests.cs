using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.Contracts;
using Codex.Contracts.Auth;

namespace Codex.AcceptanceTests.Lanes.Auth;

// /account → Agent access as JD (#32): create a token, see it once, find it listed and counted on Users, revoke it.
[TestFixture]
public partial class AgentAccessTests : SeededAcceptanceTest
{
    private const string Name = "Claude Code on the desktop";
    private string _tokenId = null!;

    [Test]
    [Order(1)]
    public async Task Create_ShouldShowTheTokenOnceAndListIt()
    {
        await Page.GotoAsync("/account");
        var access = new AgentAccessSection(Page);
        await Expect(access.Empty).ToBeVisibleAsync();

        await access.CreateAsync(Name, ["write"], "Never");

        await Expect(access.Secret).ToHaveTextAsync(Secret());
        _tokenId = (await TokensAsync()).Single().Id.ToString();
        await Expect(access.Row(_tokenId)).ToContainTextAsync(Name);
        await Expect(access.Row(_tokenId)).ToContainTextAsync("read, write");
        await Expect(access.Row(_tokenId)).ToContainTextAsync("Never");
        await access.DismissSecretAsync();
        await Expect(access.Secret).ToHaveCountAsync(0);
    }

    [Test]
    [Order(2)]
    public async Task Users_ShouldCountTheTokenAndRevokeIt()
    {
        await Page.GotoAsync("/settings/users");
        var users = new UsersScreen(Page);
        var jd = SeedIds.Users.Jd.ToString();
        await Expect(users.TokenCount(jd)).ToHaveTextAsync("1");

        await users.RevokeTokens(jd).ClickAsync();

        await Expect(users.TokenCount(jd)).ToHaveTextAsync("0");
        (await TokensAsync()).ShouldBeEmpty();
    }

    [Test]
    [Order(3)]
    public async Task Revoke_ShouldRemoveTheTokenFromTheList()
    {
        await Page.GotoAsync("/account");
        var access = new AgentAccessSection(Page);
        await access.CreateAsync("Script", [], "90 days");
        await Expect(access.Secret).ToBeVisibleAsync();
        var tokenId = (await TokensAsync()).Single().Id.ToString();

        await access.Revoke(tokenId).ClickAsync();

        await Expect(access.Row(tokenId)).ToHaveCountAsync(0);
        await Expect(access.Empty).ToBeVisibleAsync();
    }

    private async Task<List<ApiTokenDto>> TokensAsync()
    {
        using var client = Host.CreateClient();
        return (await client.GetFromJsonAsync<List<ApiTokenDto>>("/api/auth/tokens"))!;
    }

    [GeneratedRegex("^cdx_pat_[0-9A-Za-z]{43}$")]
    private static partial Regex Secret();
}
