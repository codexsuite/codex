using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class AnthropicDraftInterruptedTests : SchemaApiFixture
{
    private static readonly AnthropicStubHandler Handler = AnthropicStubHandler.Streaming(["The "], interruptedBy: "anthropic-error-overloaded-midstream.sse");

    protected override Action<IServiceCollection>? ConfigureTestServices => AnthropicDraftRows.Provider(Handler);

    [OneTimeTearDown]
    public void DisposeHandler() => Handler.Dispose();

    [Test]
    public async Task Generate_WhenAnthropicFailsAfterSomeText_ShouldEndTheStreamWithAnErrorLineAndAFailedRow()
    {
        await using var db = await OpenDatabaseAsync();
        var beatId = await AnthropicDraftRows.BeatAsync(db);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, AnthropicDraftRows.Model, null, null));
        var events = await DraftStreams.ReadAllAsync(response);

        events.Count.ShouldBe(3);
        events[1].Text.ShouldBe("The ");
        events[2].Error!.Error.ShouldContain("overloaded");
        var id = events[0].Started!.GenerationId;
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id })).ShouldBe("failed");
    }
}
