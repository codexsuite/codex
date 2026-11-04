using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Dapper;

namespace Codex.WebApi.Tests.AiIntegrations;

// SSRF (default settings): a user-supplied base URL must be https:// to public addresses only, on save and on test.
[TestFixture]
public class AiBaseUrlRefusedTests : AiIntegrationFixture
{
    [TestCase("https://10.1.2.3/v1")]
    [TestCase("https://192.168.1.5/v1")]
    [TestCase("https://127.0.0.1:8443/v1")]
    [TestCase("https://169.254.169.254/latest/meta-data")]
    [TestCase("https://[::1]/v1")]
    [TestCase("https://intranet.test/v1")]
    [TestCase("http://api.example.test/v1")]
    [TestCase("ftp://api.example.test/v1")]
    [TestCase("https://user:secret@api.example.test/v1")]
    [TestCase("https://api.example.test/v1?token=1")]
    [TestCase("https://api.example.test/v1#frag")]
    [TestCase("not a url")]
    public async Task BaseUrl_WhenNotAllowed_ShouldBeRefusedOnCreateAndTest(string baseUrl)
    {
        using var create = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(baseUrl: baseUrl));
        using var test = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", baseUrl, "sk-test-1234567890"));

        (await create.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await test.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        Providers.Connections.ShouldBeEmpty();
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM ai_integration")).ShouldBe(0);
    }

    [Test]
    public async Task BaseUrl_WhenPrivate_ShouldSayHowTheServerOwnerCanAllowIt()
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(baseUrl: "https://10.1.2.3/v1"));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Error.ShouldContain("not on the public internet");
        error.Error.ShouldContain("CODEX_AI_ALLOW_LOCAL_ENDPOINTS");
    }

    [Test]
    public async Task BaseUrl_WhenPublic_ShouldBeAccepted()
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(baseUrl: "https://gateway.example.test/openai/v1/"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AiIntegrationDto>())!.BaseUrl.ShouldBe("https://gateway.example.test/openai/v1");
    }
}
