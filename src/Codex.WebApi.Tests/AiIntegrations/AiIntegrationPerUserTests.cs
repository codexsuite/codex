using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.AiIntegrations;

// Two users each connect a provider. Neither sees or touches the other's: every route answers 404 for the other's id.
[TestFixture]
public class AiIntegrationPerUserTests : AiIntegrationFixture
{
    private static readonly Guid UserB = new("0199c0de-0000-7000-8000-0000000000b3");

    private HttpClient _clientB = null!;
    private AiIntegrationDto _jds = null!;
    private AiIntegrationDto _bs = null!;

    [OneTimeSetUp]
    public async Task CreateUserBAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "b-ai@example.test", "User B", id: UserB);
        _clientB = Host.CreateClient().ActingAs(UserB);
    }

    [OneTimeTearDown]
    public void DisposeUserB() => _clientB.Dispose();

    [Test, Order(1)]
    public async Task Create_WhenEachUserConnects_ShouldMakeEachTheirOwnDefaultAndShowOnlyTheirOwn()
    {
        _jds = await CreateAsync(Client, OpenAi());
        _bs = await CreateAsync(_clientB, OpenRouter());

        _jds.IsDefault.ShouldBeTrue();
        _bs.IsDefault.ShouldBeTrue();
        (await ListAsync(Client)).Integrations.Select(i => i.Id).ShouldBe([_jds.Id]);
        (await ListAsync(_clientB)).Integrations.Select(i => i.Id).ShouldBe([_bs.Id]);
        (await _clientB.GetFromJsonAsync<AiModelChoicesDto>("/api/ai/models"))!.Integrations.Select(c => c.IntegrationId).ShouldBe([_bs.Id]);
    }

    [Test, Order(2)]
    public async Task Routes_WhenTheIntegrationIsAnotherUsers_ShouldBeNotFound()
    {
        var id = _jds.Id;
        var responses = new[]
        {
            await _clientB.PatchAsJsonAsync($"/api/ai/integrations/{id}", new UpdateAiIntegrationRequest("Stolen", null, "sk-stolen-0000000000", null)),
            await _clientB.DeleteAsync($"/api/ai/integrations/{id}"),
            await _clientB.PostAsync($"/api/ai/integrations/{id}/default", null),
            await _clientB.PostAsync($"/api/ai/integrations/{id}/test", null),
        };

        foreach (var response in responses)
        {
            (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
            response.Dispose();
        }

        Providers.Connections.ShouldBeEmpty();
        var theirs = (await ListAsync(Client)).Integrations.Single();
        (theirs.Label, theirs.KeyHint, theirs.IsDefault).ShouldBe((_jds.Label, _jds.KeyHint, true));
    }
}
