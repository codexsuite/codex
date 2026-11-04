using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.References;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.References;

// The same JSON file is type-checked and round-tripped by src/Codex.Core.Client/src/api/quoteFlags.test.ts.
[TestFixture]
public class QuoteFlagFixtureTests
{
    [Test]
    public void QuoteFlagDtos_RoundTrip()
    {
        var original = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "references", "quote-flags.json")))!;
        var model = original.Deserialize<List<QuoteFlagDto>>(JsonSerializerOptions.Web);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original).ShouldBeTrue();
    }
}
