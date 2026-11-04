using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Codex.Mcp.Tests;

// The slash commands of #20 Part 5 over stdio on the seed: each gathers its data with the tools in-process and returns
// instructions that name the tools to use next. /resume is covered by McpReadToolsTests. Nothing here writes, so the
// tests share one server.
[TestFixture]
[CancelAfter(60000)]
public class McpPromptTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ListPrompts_WhenAsked_ShouldOfferTheFourSlashCommands()
    {
        var prompts = await Client.ListPromptsAsync(cancellationToken: McpCalls.Token);

        prompts.Select(p => p.Name).ShouldBe(["resume", "draft-scene", "print-readiness", "triage-references"], ignoreOrder: true);
    }

    [Test]
    public async Task GetPrompt_WhenDraftSceneNamesAScene_ShouldShowTheSceneAndSendTheAgentToUpsertBeatAndDraftProse()
    {
        var text = await PromptAsync("draft-scene", new Dictionary<string, object?> { ["scene"] = SeedIds.Scenes.Ch5Sc2.ToString() });

        text.ShouldContain("Ch 5");
        text.ShouldContain("\"version\"");
        text.ShouldContain("upsert_beat");
        text.ShouldContain("draft_prose({ beat })");
        text.ShouldContain("do not call resolve_draft with apply unless I tell you");
    }

    [Test]
    public async Task GetPrompt_WhenDraftSceneIsGivenADisplayNumberAndBook_ShouldFindTheScene()
    {
        var text = await PromptAsync("draft-scene", new Dictionary<string, object?> { ["scene"] = "5.2", ["book"] = ProductEngineering });

        text.ShouldContain(SeedIds.Scenes.Ch5Sc2.ToString());
    }

    [Test]
    public async Task GetPrompt_WhenDraftSceneNamesNoScene_ShouldFailNotFound()
    {
        var error = await Should.ThrowAsync<McpException>(async () => await Client.GetPromptAsync(
            "draft-scene", new Dictionary<string, object?> { ["scene"] = Guid.NewGuid().ToString() }, cancellationToken: McpCalls.Token));

        error.Message.ShouldContain("not_found");
    }

    [Test]
    public async Task GetPrompt_WhenPrintReadinessIsAsked_ShouldHandTheClearanceToTheAgentAndKeepSignOffWithTheAuthor()
    {
        var text = await PromptAsync("print-readiness", new Dictionary<string, object?> { ["book"] = ProductEngineering });

        text.ShouldContain("\"print_blocked\"");
        text.ShouldContain("one source at a time");
        text.ShouldContain("preview_layout");
        text.ShouldContain("export_for_printer");
        text.ShouldContain("preflight");
        text.ShouldContain("Do not call record_clearance_decision with sign_off or accept_fair_use");
    }

    [Test]
    public async Task GetPrompt_WhenTriageReferencesIsAsked_ShouldListTheUnflaggedQuotesAndOfferNotAQuote()
    {
        var text = await PromptAsync("triage-references", new Dictionary<string, object?> { ["book"] = ProductEngineering });

        text.ShouldContain("2 possible unflagged quote(s)");
        text.ShouldContain("\"flags\"");
        text.ShouldContain("not_a_quote");
        text.ShouldContain("cite_source({ from_flag");
        text.ShouldContain("never call record_clearance_decision with sign_off or accept_fair_use");
    }

    private async Task<string> PromptAsync(string name, Dictionary<string, object?> arguments)
    {
        var prompt = await Client.GetPromptAsync(name, arguments, cancellationToken: McpCalls.Token);
        return prompt.Messages.Single().Content.ShouldBeOfType<TextContentBlock>().Text;
    }
}
