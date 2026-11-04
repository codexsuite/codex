using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Core;
using Dapper;

namespace Codex.WebApi.Tests.AiIntegrations;

// One user's story: connect two providers, switch the default, edit one, replace its key, delete the default.
[TestFixture]
public class AiIntegrationCrudTests : AiIntegrationFixture
{
    private AiIntegrationDto _gateway = null!;
    private AiIntegrationDto _router = null!;

    [Test, Order(1)]
    public async Task Create_WhenFirst_ShouldBeTheDefaultWithAHintAndNoKey()
    {
        _gateway = await CreateAsync(Client, OpenAi(baseUrl: OpenAiUrl + "/"));

        _gateway.Models.ShouldBeEmpty();
        (_gateway with { Models = [] }).ShouldBe(_gateway with
        {
            Kind = "openai_compatible", Label = "My gateway", BaseUrl = OpenAiUrl, KeyHint = "sk-…2222", DefaultModel = null,
            Models = [], IsDefault = true, CreatedLabel = DisplayDate.Relative(TimeProvider.System.GetUtcNow(), TimeProvider.System),
            LastUsedLabel = null, LastError = null,
        });
    }

    [Test, Order(2)]
    public async Task Create_WhenSecond_ShouldNotBeTheDefault()
    {
        _router = await CreateAsync(Client, OpenRouter());

        _router.IsDefault.ShouldBeFalse();
        _router.KeyHint.ShouldBe("sk-…yyyy");
        _router.BaseUrl.ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task List_ShouldShowTheDefaultFirstThenInConnectedOrder()
    {
        var settings = await ListAsync(Client);

        settings.Integrations.Select(i => i.Id).ShouldBe([_gateway.Id, _router.Id]);
        settings.LocalEndpointsAllowed.ShouldBeFalse();
    }

    [Test, Order(4)]
    public async Task MakeDefault_ShouldMoveTheDefaultToTheOtherIntegration()
    {
        var response = await Client.PostAsync($"/api/ai/integrations/{_router.Id}/default", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await response.Content.ReadFromJsonAsync<AiSettingsDto>())!;
        settings.Integrations.Select(i => (i.Id, i.IsDefault)).ShouldBe([(_router.Id, true), (_gateway.Id, false)]);
    }

    [Test, Order(5)]
    public async Task Patch_ShouldChangeTheLabelAndDefaultModelAndLeaveTheRestAlone()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/ai/integrations/{_gateway.Id}", new UpdateAiIntegrationRequest("Renamed", null, null, " gpt-5 "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var patched = (await response.Content.ReadFromJsonAsync<AiIntegrationDto>())!;
        (patched with { Models = [] }).ShouldBe(_gateway with { Label = "Renamed", DefaultModel = "gpt-5", IsDefault = false, Models = [] });
    }

    [Test, Order(6)]
    public async Task Patch_WhenTheKeyIsReplaced_ShouldShowTheNewHintAndUseTheNewKeyOnTest()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/ai/integrations/{_gateway.Id}", new UpdateAiIntegrationRequest(null, null, "sk-new-CCCC3333DDDD4444", null));
        var patched = (await response.Content.ReadFromJsonAsync<AiIntegrationDto>())!;
        using var test = await Client.PostAsync($"/api/ai/integrations/{_gateway.Id}/test", null);

        patched.KeyHint.ShouldBe("sk-…4444");
        patched.Label.ShouldBe("Renamed");
        test.StatusCode.ShouldBe(HttpStatusCode.OK);
        Providers.LastConnection.ApiKey.ShouldBe("sk-new-CCCC3333DDDD4444");
    }

    [Test, Order(7)]
    public async Task Patch_WhenTheDefaultModelIsEmpty_ShouldClearIt()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/ai/integrations/{_gateway.Id}", new UpdateAiIntegrationRequest(null, null, null, ""));

        (await response.Content.ReadFromJsonAsync<AiIntegrationDto>())!.DefaultModel.ShouldBeNull();
    }

    [Test, Order(8)]
    public async Task Models_ShouldListEachIntegrationWithItsModelsAndDefaultFirst()
    {
        var choices = (await Client.GetFromJsonAsync<AiModelChoicesDto>("/api/ai/models"))!;

        choices.Integrations.Select(c => (c.IntegrationId, c.Kind, c.IsDefault)).ShouldBe(
            [(_router.Id, "openrouter", true), (_gateway.Id, "openai_compatible", false)]);
        choices.Integrations[1].Models.ShouldBe(["model-a", "model-b"], ignoreOrder: true);
    }

    [Test, Order(9)]
    public async Task Delete_WhenTheDefault_ShouldMakeTheOtherOneTheDefault()
    {
        using var response = await Client.DeleteAsync($"/api/ai/integrations/{_router.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var settings = await ListAsync(Client);
        settings.Integrations.Select(i => (i.Id, i.IsDefault)).ShouldBe([(_gateway.Id, true)]);
    }

    [Test, Order(10)]
    public async Task Delete_WhenAlreadyGone_ShouldBeNotFound()
    {
        using var response = await Client.DeleteAsync($"/api/ai/integrations/{_router.Id}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(11)]
    public async Task Delete_ShouldArchiveTheRowWithABlankKeyAndHideItFromEveryUse()
    {
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(DateTime? ArchivedAt, string Credential, bool IsDefault)>(
            "SELECT archived_at, credential, is_default FROM ai_integration WHERE id = @Id", new { _router.Id });
        (row.ArchivedAt.HasValue, row.Credential, row.IsDefault).ShouldBe((true, "", false));

        using var test = await Client.PostAsync($"/api/ai/integrations/{_router.Id}/test", null);
        using var makeDefault = await Client.PostAsync($"/api/ai/integrations/{_router.Id}/default", null);
        using var update = await Client.PatchAsJsonAsync($"/api/ai/integrations/{_router.Id}", new UpdateAiIntegrationRequest("Revived", null, null, null));

        await test.ReadApiErrorAsync(HttpStatusCode.NotFound);
        await makeDefault.ReadApiErrorAsync(HttpStatusCode.NotFound);
        await update.ReadApiErrorAsync(HttpStatusCode.NotFound);
        (await ListAsync(Client)).Integrations.ShouldNotContain(i => i.Id == _router.Id);
    }
}
