using System.Net;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class AnthropicDraftOverloadedTests : SchemaApiFixture
{
    private static readonly AnthropicStubHandler Handler = AnthropicStubHandler.Error((HttpStatusCode)529, "anthropic-529.json");

    protected override Action<IServiceCollection>? ConfigureTestServices => AnthropicDraftRows.Provider(Handler);

    [OneTimeTearDown]
    public void DisposeHandler() => Handler.Dispose();

    [Test]
    public async Task Generate_WhenAnthropicIsOverloadedBeforeAnyText_ShouldReturnTheMessageAndLeaveAFailedRow()
    {
        await using var db = await OpenDatabaseAsync();
        var beatId = await AnthropicDraftRows.BeatAsync(db);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, AnthropicDraftRows.Model, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.InternalServerError);
        error.Error.ShouldContain("overloaded");
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @beatId", new { beatId })).ShouldBe("failed");
    }
}
