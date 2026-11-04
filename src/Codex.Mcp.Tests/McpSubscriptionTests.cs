using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;
using Dapper;
using ModelContextProtocol;
using Npgsql;

namespace Codex.Mcp.Tests;

// Resource subscriptions over the real stdio process (#20 Part 5). A write through a tool in the same session announces
// the resources it changed and no others; another user's resource, or one that is not subscribable, is refused. The test
// waits on the notification (a TaskCompletionSource), never sleeps: notifications arrive in order, so "A then B" proves
// the first write did not also announce B.
[TestFixture]
[CancelAfter(90000)]
public partial class McpSubscriptionTests
{
    private static readonly string Ch4Sc1 = $"codex://scene/{SeedIds.Scenes.Ch4Sc1}";
    private static readonly string Ch3Sc1 = $"codex://scene/{SeedIds.Scenes.Ch3Sc1}";
    private static readonly string Outline = $"codex://book/{SeedIds.Books.ProductEngineering}/outline";
    private const string UserBEmail = "b@example.test";

    private McpStdioServer _server = null!;
    private McpStdioServer _userB = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        _userB = await McpStdioServer.StartAsUserAsync(async database =>
        {
            await TestUsers.CreateAsync(database.ConnectionString, UserBEmail, "User B");
            return UserBEmail;
        });
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _server.DisposeAsync();
        await _userB.DisposeAsync();
    }

    [Test]
    public async Task Listen_WhenASceneIsEdited_ShouldAnnounceThatSceneAndNotAnUnrelatedOne()
    {
        await using var listen = await McpListen.StartAsync(_server.Client, Ch4Sc1, Ch3Sc1);

        await EditAsync(SeedIds.Scenes.Ch4Sc1);
        var first = await listen.NextAsync();
        await EditAsync(SeedIds.Scenes.Ch3Sc1);
        var second = await listen.NextAsync();

        // In order: the first write announced only its own scene, or the second read would have been Ch3Sc1 early.
        new[] { first, second }.ShouldBe([Ch4Sc1, Ch3Sc1]);
    }

    [Test]
    public void Initialize_WhenOnlyListenIsHandled_ShouldNotAdvertiseTheRetiredSubscribeCapability()
    {
        // resources.subscribe is the capability of resources/subscribe, which this server does not handle.
        _server.Client.ServerCapabilities.Resources?.Subscribe.ShouldNotBe(true);
    }

    [Test]
    public async Task Listen_WhenAskedForResourcesItCanHonour_ShouldAcknowledgeThemAll()
    {
        await using var listen = await McpListen.StartAsync(_server.Client, Outline, Ch4Sc1);

        listen.Acknowledged["notifications"]!["resourceSubscriptions"]!.AsArray().Select(n => n!.GetValue<string>())
            .ShouldBe([Outline, Ch4Sc1], ignoreOrder: true);
    }

    [Test]
    public async Task Listen_WhenTheOutlineIsSubscribedAndAProseSaveHappens_ShouldAnnounceTheOutline()
    {
        await using var listen = await McpListen.StartAsync(_server.Client, Outline);

        await EditAsync(SeedIds.Scenes.Ch4Sc1);

        (await listen.NextAsync()).ShouldBe(Outline);
    }

    [Test]
    public async Task Listen_WhenTheResourceIsAnotherUsers_ShouldFailNotFound()
    {
        var error = await Should.ThrowAsync<McpException>(() => McpListen.StartAsync(_userB.Client, Ch4Sc1));

        error.Message.ShouldContain("not_found");
    }

    [TestCase("codex://book/00000000-0000-0000-0000-000000000000/outline")]
    [TestCase("codex://nothing")]
    public async Task Listen_WhenTheUriIsNotASubscribableResource_ShouldFailNotFound(string uri)
    {
        var error = await Should.ThrowAsync<McpException>(() => McpListen.StartAsync(_server.Client, uri));

        error.Message.ShouldContain("not_found");
    }

    private async Task EditAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        var (body, version) = await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId });
        var phrase = PlainWords().Matches(body).Select(m => m.Value.Trim()).First(p => Regex.Count(body, Regex.Escape(p)) == 1);
        await _server.Client.OkAsync("edit_prose", new
        {
            scene = sceneId.ToString(), expected_version = version, edits = new[] { new { old = phrase, @new = "subscribed" } },
        });
    }

    [GeneratedRegex(@"\b[A-Za-z]{6,}\b")]
    private static partial Regex PlainWords();
}
