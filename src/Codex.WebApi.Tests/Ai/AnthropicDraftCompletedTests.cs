using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class AnthropicDraftCompletedTests : SchemaApiFixture
{
    private static readonly AnthropicStubHandler Handler = AnthropicStubHandler.Streaming(["The ", "door ", "creaked."]);

    protected override Action<IServiceCollection>? ConfigureTestServices => AnthropicDraftRows.Provider(Handler);

    [OneTimeTearDown]
    public void DisposeHandler() => Handler.Dispose();

    [Test]
    public async Task Generate_WhenTheProviderStreams_ShouldEmitTheChunksAndRecordTheTokenCounts()
    {
        await using var db = await OpenDatabaseAsync();
        var beatId = await AnthropicDraftRows.BeatAsync(db);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, AnthropicDraftRows.Model, null, null));
        var events = await DraftStreams.ReadAllAsync(response);

        events.Select(e => e.Text).Where(t => t != null).ShouldBe(["The ", "door ", "creaked."]);
        var final = events[^1].Final.ShouldNotBeNull();
        (final.InputTokens, final.OutputTokens).ShouldBe((25, 6));
        var row = await db.QuerySingleAsync<(string Status, string Output, int InputTokens, int OutputTokens)>(
            "SELECT status, output, input_tokens, output_tokens FROM ai_generation WHERE id = @id", new { id = final.GenerationId });
        row.ShouldBe(("pending", "The door creaked.", 25, 6));
        Handler.ApiKeyHeader.ShouldBe(AnthropicDraftRows.Key);
    }
}
