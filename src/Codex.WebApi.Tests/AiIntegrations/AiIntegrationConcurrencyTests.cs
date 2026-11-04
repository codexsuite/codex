using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai.Integrations;
using Dapper;

namespace Codex.WebApi.Tests.AiIntegrations;

// The one-default rule under concurrent writers (#36): creates, default switches and a delete-then-create never 500
// and never leave a user with integrations but no default (or two).
[TestFixture]
public class AiIntegrationConcurrencyTests : AiIntegrationFixture
{
    private const int Writers = 8;

    [SetUp]
    public async Task ClearAsync()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("DELETE FROM ai_integration");
    }

    [Test]
    public async Task Create_WhenManyRunAtOnceForAFreshUser_ShouldAllSucceedWithExactlyOneDefault()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Client.PostAsJsonAsync("/api/ai/integrations", OpenAi() with { Label = $"Gateway {i}" })));

        responses.Select(r => r.StatusCode).ShouldAllBe(code => code == HttpStatusCode.OK);
        await ShouldHaveOneDefaultAmongAsync(Writers);
    }

    [Test]
    public async Task MakeDefault_WhenTwoSwitchAtOnce_ShouldNotFailAndKeepExactlyOneDefault()
    {
        var a = await CreateAsync(Client, OpenAi() with { Label = "A" });
        var b = await CreateAsync(Client, OpenAi() with { Label = "B" });

        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Client.PostAsync($"/api/ai/integrations/{(i % 2 == 0 ? a.Id : b.Id)}/default", null)));

        responses.Select(r => r.StatusCode).ShouldAllBe(code => code == HttpStatusCode.OK);
        await ShouldHaveOneDefaultAmongAsync(2);
    }

    [Test]
    public async Task DeleteDefault_WhenCreatesRunAtTheSameTime_ShouldLeaveExactlyOneDefault()
    {
        var first = await CreateAsync(Client, OpenAi() with { Label = "First" });
        await CreateAsync(Client, OpenAi() with { Label = "Second" });

        var tasks = new List<Task<HttpResponseMessage>> { Client.DeleteAsync($"/api/ai/integrations/{first.Id}") };
        tasks.AddRange(Enumerable.Range(0, 4).Select(i => Client.PostAsJsonAsync("/api/ai/integrations", OpenAi() with { Label = $"New {i}" })));
        var responses = await Task.WhenAll(tasks);

        responses.Select(r => (int)r.StatusCode).ShouldAllBe(code => code < 300);
        await ShouldHaveOneDefaultAmongAsync(5);
    }

    [Test]
    public async Task Create_WhenTheUserHasIntegrationsButNoDefault_ShouldMakeTheNewOneDefault()
    {
        await CreateAsync(Client, OpenAi() with { Label = "Orphan" });
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("UPDATE ai_integration SET is_default = false");
        }

        var created = await CreateAsync(Client, OpenAi() with { Label = "Rescuer" });

        created.IsDefault.ShouldBeTrue();
        await ShouldHaveOneDefaultAmongAsync(2);
    }

    private async Task ShouldHaveOneDefaultAmongAsync(int expected)
    {
        var settings = await ListAsync(Client);
        settings.Integrations.Count.ShouldBe(expected);
        settings.Integrations.Count(i => i.IsDefault).ShouldBe(1);
    }
}
