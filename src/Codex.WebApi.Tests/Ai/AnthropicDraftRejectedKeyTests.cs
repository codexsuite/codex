using System.Net;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Codex.Contracts.Ai;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class AnthropicDraftRejectedKeyTests : SchemaApiFixture
{
    private static readonly AnthropicStubHandler Handler = AnthropicStubHandler.Error(HttpStatusCode.Unauthorized, "anthropic-401.json");

    protected override Action<IServiceCollection>? ConfigureTestServices => AnthropicDraftRows.Provider(Handler);

    [OneTimeTearDown]
    public void DisposeHandler() => Handler.Dispose();

    [Test]
    public async Task Generate_WhenAnthropicRejectsTheKey_ShouldReturn503NotConfiguredAndLeaveAFailedRow()
    {
        await using var db = await OpenDatabaseAsync();
        var beatId = await AnthropicDraftRows.BeatAsync(db);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, AnthropicDraftRows.Model, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.ServiceUnavailable);
        error.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        error.Error.ShouldNotContain(AnthropicDraftRows.Key);
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @beatId", new { beatId })).ShouldBe("failed");
    }
}
