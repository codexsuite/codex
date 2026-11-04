using Codex.Application.Mcp;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Fakes;

namespace Codex.Mcp.Tests;

// M9: a session can start 20 Codex generations; the 21st is rate_limited. submit_draft is not a Codex generation.
[TestFixture]
[CancelAfter(120000)]
public class McpDraftBudgetTests
{
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();

    [Test]
    public async Task DraftProse_WhenTheSessionHasStartedTwentyGenerations_ShouldRateLimitTheTwentyFirst()
    {
        await using var server = await McpInProcessServer.StartAsync(new FakeProseGenerator(["Words."]));
        for (var i = 0; i < 20; i++)
        {
            await server.Client.OkAsync("draft_prose", new { scene = Scene });
        }

        var error = await server.Client.ErrorAsync("draft_prose", new { scene = Scene });

        error.GetProperty("code").GetString().ShouldBe("rate_limited");
        await server.Client.OkAsync("submit_draft", new { scene = Scene, text = "Still allowed." });
    }

    [Test]
    public async Task DraftProse_WhenTheLimitIsSetInConfiguration_ShouldUseIt()
    {
        await using var server = await McpInProcessServer.StartAsync(
            new FakeProseGenerator(["Words."]), new Dictionary<string, string?> { [McpGenerationBudget.Setting] = "2" });
        await server.Client.OkAsync("draft_prose", new { scene = Scene });
        await server.Client.OkAsync("draft_prose", new { scene = Scene });

        var error = await server.Client.ErrorAsync("draft_prose", new { scene = Scene });

        error.GetProperty("code").GetString().ShouldBe("rate_limited");
    }
}
