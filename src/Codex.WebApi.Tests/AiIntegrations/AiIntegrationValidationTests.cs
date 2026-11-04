using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Dapper;

namespace Codex.WebApi.Tests.AiIntegrations;

// What a Connect or Edit request must have: a known kind, a name, a key, and a base URL exactly when the kind takes one.
[TestFixture]
public class AiIntegrationValidationTests : AiIntegrationFixture
{
    private static IEnumerable<TestCaseData> BadCreates()
    {
        yield return new TestCaseData(new CreateAiIntegrationRequest("openai_oauth", "x", null, "sk-1234567890", null)).SetName("unknown kind");
        yield return new TestCaseData(new CreateAiIntegrationRequest("", "x", null, "sk-1234567890", null)).SetName("empty kind");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openai_compatible", "x", null, "sk-1234567890", null)).SetName("no base url for openai_compatible");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openai_compatible", "x", "  ", "sk-1234567890", null)).SetName("blank base url for openai_compatible");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "x", "https://openrouter.ai/api/v1", "sk-1234567890", null)).SetName("base url for openrouter");
        yield return new TestCaseData(new CreateAiIntegrationRequest("anthropic", "x", "https://api.anthropic.com", "sk-1234567890", null)).SetName("base url for anthropic");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "x", null, "", null)).SetName("empty key");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "x", null, "   ", null)).SetName("blank key");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "x", null, new string('k', 1001), null)).SetName("key too long");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "", null, "sk-1234567890", null)).SetName("empty label");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", "  ", null, "sk-1234567890", null)).SetName("blank label");
        yield return new TestCaseData(new CreateAiIntegrationRequest("openrouter", new string('l', 101), null, "sk-1234567890", null)).SetName("label too long");
    }

    [TestCaseSource(nameof(BadCreates))]
    public async Task Create_WhenTheRequestIsInvalid_ShouldBeValidationAndStoreNothing(CreateAiIntegrationRequest request)
    {
        await using var db = await OpenDatabaseAsync();
        var before = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM ai_integration");

        using var response = await Client.PostAsJsonAsync("/api/ai/integrations", request);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM ai_integration")).ShouldBe(before);
    }

    [Test]
    public async Task Create_WhenLabelAndKeyHaveSpaces_ShouldTrimThem()
    {
        var created = await CreateAsync(Client, new CreateAiIntegrationRequest("openrouter", "  Padded  ", null, "  sk-or-v1-trim1234  ", " some/model "));

        created.Label.ShouldBe("Padded");
        created.KeyHint.ShouldBe("…1234");
        created.DefaultModel.ShouldBe("some/model");
    }

    [Test]
    public async Task Patch_WhenTheRequestIsInvalid_ShouldBeValidationAndChangeNothing()
    {
        var created = await CreateAsync(Client, OpenRouter());

        using var label = await Client.PatchAsJsonAsync($"/api/ai/integrations/{created.Id}", new UpdateAiIntegrationRequest("  ", null, null, null));
        using var key = await Client.PatchAsJsonAsync($"/api/ai/integrations/{created.Id}", new UpdateAiIntegrationRequest(null, null, "", null));
        using var baseUrl = await Client.PatchAsJsonAsync($"/api/ai/integrations/{created.Id}", new UpdateAiIntegrationRequest(null, "https://x.example.test", null, null));

        foreach (var response in new[] { label, key, baseUrl })
        {
            (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }

        (await ListAsync(Client)).Integrations.Single(i => i.Id == created.Id).Label.ShouldBe(created.Label);
    }

    [Test]
    public async Task Patch_WhenTheBaseUrlIsPrivate_ShouldBeRefused()
    {
        var created = await CreateAsync(Client, OpenAi());

        using var response = await Client.PatchAsJsonAsync($"/api/ai/integrations/{created.Id}", new UpdateAiIntegrationRequest(null, "https://192.168.0.7/v1", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await ListAsync(Client)).Integrations.Single(i => i.Id == created.Id).BaseUrl.ShouldBe(OpenAiUrl);
    }

    [TestCase("")]
    [TestCase("openai_oauth")]
    public async Task Test_WhenTheKindIsUnknown_ShouldBeValidation(string kind)
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest(kind, null, "sk-1234567890"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Test_WhenTheKeyIsEmptyOrOpenAiHasNoBaseUrl_ShouldBeValidation()
    {
        using var noKey = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openrouter", null, " "));
        using var noUrl = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", null, "sk-1234567890"));

        (await noKey.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await noUrl.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
