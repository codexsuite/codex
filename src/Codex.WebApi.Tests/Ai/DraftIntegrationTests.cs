using System.Net;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Ai;

// A draft written through the user's own AI integrations (#36). Provider calls go to a recording fake (no network);
// the integrations are rows with keys encrypted by the host's own protector. One scenario, in order.
[TestFixture]
public class DraftIntegrationTests : SchemaApiFixture
{
    private const string DefaultKey = "sk-ant-default-secret-AAAA";
    private const string OtherKey = "sk-other-secret-BBBBBBBB";
    private const string StrangerKey = "sk-stranger-secret-CCCC";

    private static readonly RecordingAiProviders Providers = new();
    private static readonly CapturedLogs Logs = new();

    private Guid _bookId;
    private Guid _beatId;
    private Guid _defaultId;
    private Guid _otherId;
    private Guid _strangerId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddSingleton<IAiProviders>(Providers);
        services.AddSingleton<ILoggerProvider>(Logs);
    };

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, var sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, sceneId);
        await TestRows.ProseAsync(db, sceneId, $"::beat{{id=\"{_beatId}\"}}\n");
        var protector = Host.Services.GetRequiredService<IAiCredentialProtector>();
        _defaultId = await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "Mine", DefaultKey, defaultModel: "default-model", isDefault: true);
        _otherId = await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "Gateway", OtherKey, "openai_compatible", "https://llm.example.test/v1");
        var stranger = await db.ExecuteScalarAsync<Guid>("INSERT INTO app_user (email, display_name) VALUES ('stranger@example.test', 'Stranger') RETURNING id");
        _strangerId = await AiIntegrationRows.InsertAsync(db, protector, stranger, "Theirs", StrangerKey, isDefault: true);
    }

    [Test]
    [Order(1)]
    public async Task Generate_WhenNoIntegrationIsNamed_ShouldUseTheDefaultWithItsKeyKindAndDefaultModel()
    {
        var events = await GenerateAsync(new GenerateDraftRequest(null, "", null, null));

        events[^1].Final.ShouldNotBeNull();
        var connection = Providers.Connections[^1];
        connection.Kind.ShouldBe("anthropic");
        connection.ApiKey.ShouldBe(DefaultKey);
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(Guid IntegrationId, string Model)>(
            "SELECT integration_id, model FROM ai_generation WHERE id = @id", new { id = events[0].Started!.GenerationId });
        row.ShouldBe((_defaultId, "default-model"));
        events[0].Started!.Model.ShouldBe("default-model");
        (await db.ExecuteScalarAsync<DateTime?>("SELECT last_used_at FROM ai_integration WHERE id = @_defaultId", new { _defaultId })).ShouldNotBeNull();
    }

    [Test]
    [Order(2)]
    public async Task Generate_WhenTheRequestNamesAnIntegration_ShouldUseItInsteadOfTheDefault()
    {
        var events = await GenerateAsync(new GenerateDraftRequest(null, " their-model ", null, null, _otherId));

        var connection = Providers.Connections[^1];
        connection.Kind.ShouldBe("openai_compatible");
        connection.BaseUrl.ShouldBe(new Uri("https://llm.example.test/v1"));
        connection.ApiKey.ShouldBe(OtherKey);
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(Guid IntegrationId, string Model)>(
            "SELECT integration_id, model FROM ai_generation WHERE id = @id", new { id = events[0].Started!.GenerationId });
        row.ShouldBe((_otherId, "their-model"));
        // The draft says which integration wrote it, so the panel's Retry asks the same account.
        var drafts = await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts");
        drafts.Drafts.Single(d => d.Id == events[0].Started!.GenerationId).IntegrationId.ShouldBe(_otherId);
    }

    [Test]
    [Order(3)]
    public async Task Generate_WhenTheIntegrationBelongsToAnotherUser_ShouldReturn404AndNeverCallTheProvider()
    {
        var calls = Providers.Connections.Count;

        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, "m", null, null, _strangerId));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        Providers.Connections.Count.ShouldBe(calls);
    }

    [Test]
    [Order(4)]
    public async Task Generate_WhenTheProviderFails_ShouldSetLastErrorAndTheNextSuccessShouldClearIt()
    {
        Providers.Next = new FakeProseGenerator(["Half. "], null, ApiException.NotConfigured("The AI provider rejected your key."));
        var failed = await GenerateAsync(new GenerateDraftRequest(null, "m", null, null));
        failed[^1].Error!.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string?>("SELECT last_error FROM ai_integration WHERE id = @_defaultId", new { _defaultId })).ShouldBe("The AI provider rejected your key.");

        Providers.Next = new FakeProseGenerator(["Fine. "]);
        await GenerateAsync(new GenerateDraftRequest(null, "m", null, null));

        (await db.ExecuteScalarAsync<string?>("SELECT last_error FROM ai_integration WHERE id = @_defaultId", new { _defaultId })).ShouldBeNull();
    }

    [Test]
    [Order(5)]
    public async Task Generate_Always_ShouldKeepEveryKeyOutOfTheStreamTheRowsAndTheLogs()
    {
        Providers.Next = new FakeProseGenerator(["Words. "]);
        var events = await GenerateAsync(new GenerateDraftRequest(null, "m", null, null));

        await using var db = await OpenDatabaseAsync();
        var rows = string.Concat(await db.QueryAsync<string>("SELECT request::text || coalesce(output, '') FROM ai_generation"));
        var everything = string.Concat(events.Select(e => System.Text.Json.JsonSerializer.Serialize(e))) + rows + Logs.Text;
        foreach (var key in new[] { DefaultKey, OtherKey, StrangerKey })
        {
            everything.ShouldNotContain(key);
        }
    }

    private async Task<List<DraftStreamEvent>> GenerateAsync(GenerateDraftRequest request)
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await DraftStreams.ReadAllAsync(response);
    }
}
