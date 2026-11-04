using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai.Integrations;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.AiIntegrations;

// The key is write-only: it appears in no API answer and no log line, and is stored encrypted.
[TestFixture]
public class AiKeyLeakTests : AiIntegrationFixture
{
    private const string Key = "sk-secret-LEAKCHECK0123456789";
    private const string ReplacementKey = "sk-secret-REPLACED9876543210";

    private readonly List<string> _bodies = [];

    private async Task<string> RecordAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _bodies.Add(body);
        return body;
    }

    [Test, Order(1)]
    public async Task Requests_ShouldNeverEchoTheKey()
    {
        using var create = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(Key));
        var created = System.Text.Json.JsonSerializer.Deserialize<AiIntegrationDto>(await RecordAsync(create), System.Text.Json.JsonSerializerOptions.Web)!;
        await RecordAsync(await Client.GetAsync("/api/ai/integrations"));
        await RecordAsync(await Client.GetAsync("/api/ai/models"));
        await RecordAsync(await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", OpenAiUrl, Key)));
        await RecordAsync(await Client.PostAsync($"/api/ai/integrations/{created.Id}/test", null));
        await RecordAsync(await Client.PatchAsJsonAsync($"/api/ai/integrations/{created.Id}", new UpdateAiIntegrationRequest(null, null, ReplacementKey, null)));
        Providers.Failure = Codex.Contracts.Errors.ApiException.NotConfigured("The key was rejected.");
        await RecordAsync(await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", OpenAiUrl, Key)));
        await RecordAsync(await Client.PostAsync($"/api/ai/integrations/{created.Id}/test", null));
        await RecordAsync(await Client.PostAsJsonAsync("/api/ai/integrations", new CreateAiIntegrationRequest("openai_compatible", "Bad", "http://insecure.example.test", Key, null)));
        await RecordAsync(await Client.PostAsJsonAsync("/api/ai/integrations", new CreateAiIntegrationRequest("nope", "Bad", null, Key, null)));

        _bodies.Count.ShouldBe(10);
        _bodies.ShouldAllBe(b => !b.Contains(Key) && !b.Contains(ReplacementKey) && !b.Contains("LEAKCHECK") && !b.Contains("REPLACED9876"));
        Logs.Lines.ShouldAllBe(l => !l.Contains("LEAKCHECK") && !l.Contains("REPLACED9876"));
    }

    [Test, Order(2)]
    public async Task Credential_ShouldBeEncryptedAndDecryptToTheKey()
    {
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(string Credential, string Hint)>("SELECT credential, key_hint FROM ai_integration");

        row.Credential.ShouldNotContain("LEAKCHECK");
        row.Credential.ShouldNotBe(ReplacementKey);
        row.Hint.ShouldBe("sk-…3210");
        Host.Services.GetRequiredService<IAiCredentialProtector>().Unprotect(row.Credential).ShouldBe(ReplacementKey);
        (await Client.GetAsync("/api/ai/integrations")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
