using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Manuscript;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Manuscript;

// The same JSON file is type-checked and round-tripped by src/Codex.Core.Client/src/api/write.test.ts.
[TestFixture]
public class ManuscriptDtoFixtureTests
{
    [Test]
    public void ManuscriptDto_RoundTrips()
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "manuscript", "manuscript.json")))!;
        var model = original.Deserialize<ManuscriptDto>(JsonSerializerOptions.Web);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original).ShouldBeTrue();
    }

    [Test]
    public void BookKbAdditionsDto_RoundTrips()
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "manuscript", "kb-additions.json")))!;
        var model = original.Deserialize<BookKbAdditionsDto>(JsonSerializerOptions.Web);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original).ShouldBeTrue();
    }
}
