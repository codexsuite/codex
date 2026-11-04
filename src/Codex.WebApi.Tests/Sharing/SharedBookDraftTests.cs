using System.Net;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fakes;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Tests.Ai;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Sharing;

// Drafting in a shared book (#80): the draft runs on the drafting user's own AI integration (#36), never the book
// owner's, and a draft whose author loses write access while it streams fails instead of being kept. Ana has write on
// the Forsaken Gods series (seed 09_sharing); JD owns it. Provider calls go to a recording fake. In order.
[TestFixture]
public class SharedBookDraftTests : ApiFixture
{
    private const string JdKey = "sk-ant-owner-secret-JJJJ";
    private const string AnaKey = "sk-ant-drafter-secret-AAAA";
    private static readonly Guid Scene = SeedIds.Scenes.PlayersGuideFigureScene;

    private static readonly RecordingAiProviders Providers = new();

    private Guid _anasIntegration;

    protected override TestUser SignedInAs => TestUser.Of(SeedIds.Users.Ana);

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IAiProviders>(Providers);

    [OneTimeSetUp]
    public async Task InsertIntegrationsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        var protector = Host.Services.GetRequiredService<IAiCredentialProtector>();
        await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "JD's", JdKey, defaultModel: "owner-model", isDefault: true);
        _anasIntegration = await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Ana, "Ana's", AnaKey, defaultModel: "drafter-model", isDefault: true);
    }

    [Test, Order(1)]
    public async Task Continue_WhenTheBookIsSharedToWrite_ShouldUseTheDraftersIntegration()
    {
        Providers.Next = new FakeProseGenerator(["Ana's words. "]);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/scenes/{Scene}/continue", new GenerateDraftRequest(null, "", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var events = await DraftStreams.ReadAllAsync(response);
        events[^1].Final.ShouldNotBeNull();
        Providers.Connections[^1].ApiKey.ShouldBe(AnaKey);
        events[0].Started!.Model.ShouldBe("drafter-model");
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(Guid IntegrationId, Guid CreatedBy, string Status)>(
            "SELECT integration_id, created_by, status FROM ai_generation WHERE id = @id", new { id = events[0].Started!.GenerationId });
        row.ShouldBe((_anasIntegration, SeedIds.Users.Ana, "pending"));
    }

    [Test, Order(2)]
    public async Task Continue_WhenTheDrafterLosesWriteAccessMidStream_ShouldFailTheDraft()
    {
        var gate = new TaskCompletionSource();
        Providers.Next = new FakeProseGenerator(["First. ", "Second."], gate.Task);

        using var response = await DraftStreams.OpenAsync(Client, $"/api/scenes/{Scene}/continue", new GenerateDraftRequest(null, "", null, null));
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var started = (await DraftStreams.NextAsync(reader))!.Started!;
        (await DraftStreams.NextAsync(reader))!.Text.ShouldBe("First. ");
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync(
                "DELETE FROM book_grant WHERE book_id = @series AND user_id = @ana", new { series = SeedIds.Series.ForsakenGods, ana = SeedIds.Users.Ana });
        }
        gate.SetResult();
        var rest = new List<DraftStreamEvent>();
        while (await DraftStreams.NextAsync(reader) is { } line)
        {
            rest.Add(line);
        }

        rest[^1].Error.ShouldNotBeNull().Code.ShouldBe(ApiErrorCodes.Forbidden);
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = started.GenerationId })).ShouldBe("failed");
    }

    [Test, Order(3)]
    public async Task Continue_WhenTheBookIsSharedToView_ShouldReturn403AndNeverCallTheProvider()
    {
        var calls = Providers.Connections.Count;

        using var response = await DraftStreams.OpenAsync(
            Client, $"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/continue", new GenerateDraftRequest(null, "", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
        Providers.Connections.Count.ShouldBe(calls);
    }
}
