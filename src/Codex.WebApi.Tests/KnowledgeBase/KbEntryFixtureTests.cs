using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.KnowledgeBase;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.KnowledgeBase;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/kbEntries.test.ts.
[TestFixture]
public class KbEntryFixtureTests
{
    [Test]
    public void KbEntryDto_RoundTrips()
    {
        var original = Fixture("kb/entry.json");
        var model = original.Deserialize<KbEntryDto>(JsonSerializerOptions.Web);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original).ShouldBeTrue();
    }

    [Test]
    public void KbEntryRequests_RoundTrip()
    {
        var original = Fixture("kb/entry-requests.json");

        RoundTrip<CreateKbEntryRequest>(original, "create").ShouldBeTrue();
        RoundTrip<PatchKbEntryRequest>(original, "patch").ShouldBeTrue();
        RoundTrip<MoveKbEntryRequest>(original, "move").ShouldBeTrue();
        RoundTrip<CreateKbCategoryRequest>(original, "category").ShouldBeTrue();
    }

    [Test]
    public void KbEntryLifecycleDtos_RoundTrip()
    {
        var original = Fixture("kb/entry-lifecycle.json");

        RoundTrip<AddKbRelatedRequest>(original, "addRelated").ShouldBeTrue();
        RoundTrip<MakeKbMentionRequest>(original, "makeMention").ShouldBeTrue();
        RoundTrip<KbDeleteImpactDto>(original, "impact").ShouldBeTrue();
        RoundTrip<KbUndoRecordDto>(original, "undo").ShouldBeTrue();
    }

    private static bool RoundTrip<T>(JsonNode original, string key) =>
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(original[key].Deserialize<T>(JsonSerializerOptions.Web), JsonSerializerOptions.Web), original[key]);

    private static JsonNode Fixture(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
}
