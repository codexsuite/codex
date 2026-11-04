using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Prompts;
using Codex.Contracts.Sources;
using Codex.TestSupport;
using Codex.TestSupport.Fakes;

namespace Codex.WebApi.Tests.Prompts;

// The prompts fixture is also type-checked by src/Codex.Core.Client/src/api/prompts.test.ts.
[TestFixture]
public class PromptsDtoFixtureTests
{
    [Test]
    public void PromptsDto_RoundTrips()
    {
        RoundTrips<PromptsDto>("prompts.json").ShouldBeTrue();
    }

    [Test]
    public void SourceLookupFixture_RoundTrips()
    {
        RoundTrips<List<SourceLookupFixtureEntry>>(FakeSourceLookup.FixtureName).ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var again = JsonSerializer.SerializeToNode(original.Deserialize<T>(JsonSerializerOptions.Web), JsonSerializerOptions.Web);
        return JsonNode.DeepEquals(again, original);
    }
}
