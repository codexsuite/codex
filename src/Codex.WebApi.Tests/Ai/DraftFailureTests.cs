using System.Net;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

[TestFixture]
public class DraftFailureTests : SchemaApiFixture
{
    private static readonly FakeProseGenerator Generator = new(["Some words. "], null, new InvalidOperationException("the provider fell over"));

    private Guid _beatId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{_beatId}\"}}\n");
    }

    [Test]
    public async Task Generate_WhenTheGeneratorFailsMidStream_ShouldEndWithAnErrorLineAndAFailedRow()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, "claude-sonnet-5-5", null, null));
        var events = await DraftStreams.ReadAllAsync(response);

        events.Count.ShouldBe(3);
        events[0].Started.ShouldNotBeNull();
        events[1].Text.ShouldBe("Some words. ");
        events[2].Error!.Code.ShouldBe(ApiErrorCodes.Internal);
        events[2].Error!.Error.ShouldNotContain("fell over");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = events[0].Started!.GenerationId })).ShouldBe("failed");
    }
}

[TestFixture]
public class DraftNotConfiguredTests : SchemaApiFixture
{
    [Test]
    public async Task Generate_WhenNoProviderIsConfigured_ShouldReturn503NotConfiguredAndLeaveAFailedRow()
    {
        await using var db = await OpenDatabaseAsync();
        var (_, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{beatId}\"}}\n");

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{beatId}/generate", new GenerateDraftRequest(null, "claude-sonnet-5-5", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.ServiceUnavailable)).Code.ShouldBe(ApiErrorCodes.NotConfigured);
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @beatId", new { beatId })).ShouldBe("failed");
    }
}
