using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Dapper;

namespace Codex.WebApi.Tests.AiIntegrations;

// Test connection: the unsaved test passes what the dialog holds to the provider; the saved test uses the stored key,
// caches the models on success and keeps the failure in last_error until the next success.
[TestFixture]
public class AiConnectionTestTests : AiIntegrationFixture
{
    private AiIntegrationDto _saved = null!;

    [Test, Order(1)]
    public async Task TestUnsaved_ShouldGiveTheProviderTheKindBaseUrlAndKeyAndReturnItsModels()
    {
        Providers.Models = ["gpt-b", "gpt-a"];

        using var response = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", OpenAiUrl + "/", " sk-try-0123456789 "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<AiConnectionTestDto>())!;
        result.Models.ShouldBe(["gpt-b", "gpt-a"]);
        (result.Ok, result.Error).ShouldBe((true, null));
        var connection = Providers.Connections.Single();
        (connection.Kind, connection.BaseUrl?.ToString(), connection.ApiKey).ShouldBe(("openai_compatible", OpenAiUrl, "sk-try-0123456789"));
    }

    [Test, Order(2)]
    public async Task TestUnsaved_WhenTheProviderSaysNo_ShouldBeOkFalseWithItsMessage()
    {
        Providers.Failure = ApiException.NotConfigured("The key was rejected.");

        using var response = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openrouter", null, "sk-or-bad-0123456789"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<AiConnectionTestDto>())!;
        result.Models.ShouldBe([]);
        (result.Ok, result.Error).ShouldBe((false, "The key was rejected."));
        Providers.LastConnection.Kind.ShouldBe("openrouter");
        Providers.LastConnection.BaseUrl.ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task TestSaved_WhenItSucceeds_ShouldCacheTheModels()
    {
        Providers.Failure = null;
        Providers.Models = ["m-1", "m-2"];
        _saved = await CreateAsync(Client, OpenAi("sk-proj-SAVEDKEY12345678"));

        using var response = await Client.PostAsync($"/api/ai/integrations/{_saved.Id}/test", null);

        var result = (await response.Content.ReadFromJsonAsync<AiConnectionTestDto>())!;
        result.Models.ShouldBe(["m-1", "m-2"]);
        (result.Ok, result.Error).ShouldBe((true, null));
        Providers.LastConnection.ApiKey.ShouldBe("sk-proj-SAVEDKEY12345678");
        Providers.LastConnection.BaseUrl!.ToString().ShouldBe(OpenAiUrl);
        var listed = (await ListAsync(Client)).Integrations.Single();
        listed.Models.ShouldBe(["m-1", "m-2"]);
        listed.LastError.ShouldBeNull();
    }

    [Test, Order(4)]
    public async Task TestSaved_WhenItFails_ShouldSetLastErrorAndKeepTheModels()
    {
        Providers.Failure = ApiException.NotConfigured("The key was rejected.");

        using var response = await Client.PostAsync($"/api/ai/integrations/{_saved.Id}/test", null);

        var result = (await response.Content.ReadFromJsonAsync<AiConnectionTestDto>())!;
        result.Models.ShouldBe([]);
        (result.Ok, result.Error).ShouldBe((false, "The key was rejected."));
        var listed = (await ListAsync(Client)).Integrations.Single();
        listed.LastError.ShouldBe("The key was rejected.");
        listed.Models.ShouldBe(["m-1", "m-2"]);
    }

    [Test, Order(5)]
    public async Task TestSaved_WhenItSucceedsAgain_ShouldClearLastError()
    {
        Providers.Failure = null;
        Providers.Models = ["m-3"];

        using var response = await Client.PostAsync($"/api/ai/integrations/{_saved.Id}/test", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = (await ListAsync(Client)).Integrations.Single();
        listed.LastError.ShouldBeNull();
        listed.Models.ShouldBe(["m-3"]);
    }

    [Test, Order(6)]
    public async Task TestSaved_WhenTheKeyRingCannotReadTheKey_ShouldBeNotConfigured()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE ai_integration SET credential = 'not-from-this-ring' WHERE id = @id", new { _saved.Id });

        using var response = await Client.PostAsync($"/api/ai/integrations/{_saved.Id}/test", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.ServiceUnavailable)).Code.ShouldBe(ApiErrorCodes.NotConfigured);
    }
}
