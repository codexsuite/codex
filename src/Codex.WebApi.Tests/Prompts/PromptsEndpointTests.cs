using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Prompts;
using Codex.TestSupport;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Prompts;

[TestFixture]
public class PromptsEndpointTests : ApiFixture
{
    [Test]
    public async Task Get_ShouldListTemplatesAndModels()
    {
        var response = await Client.GetAsync("/api/prompts");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "prompts.json")))!;
        JsonNode.DeepEquals(body, expected).ShouldBeTrue(body.ToJsonString());
    }

    [Test]
    public async Task Get_ShouldMarkExactlyOneDefaultModel()
    {
        var dto = await Client.GetFromJsonAsync<PromptsDto>("/api/prompts", JsonSerializerOptions.Web);

        dto.ShouldNotBeNull().Models.Count(m => m.Default).ShouldBe(1);
        dto.Models.Single(m => m.Default).Id.ShouldBe("claude-sonnet-5-5");
    }
}
