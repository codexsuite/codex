using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Jobs;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.References;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.KnowledgeBase;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/kbReferencesJobs.test.ts.
[TestFixture]
public class ReadWriteDtoFixtureTests
{
    [Test]
    public void KbDto_RoundTrips()
    {
        RoundTrips<KbDto>("kb/kb.json").ShouldBeTrue();
    }

    [Test]
    public void KbRequests_RoundTrip()
    {
        var original = Fixture("kb/link-requests.json");
        var link = original["link"].Deserialize<LinkKbEntryRequest>(JsonSerializerOptions.Web);
        var context = original["beatContext"].Deserialize<SetBeatContextRequest>(JsonSerializerOptions.Web);

        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(link, JsonSerializerOptions.Web), original["link"]).ShouldBeTrue();
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(context, JsonSerializerOptions.Web), original["beatContext"])
            .ShouldBeTrue();
    }

    [Test]
    public void MarkerDtos_RoundTrip()
    {
        RoundTrips<List<MarkerDto>>("references/markers.json").ShouldBeTrue();
    }

    [Test]
    public void ClearanceSummaryDto_RoundTrips()
    {
        RoundTrips<ClearanceSummaryDto>("references/clearance-summary.json").ShouldBeTrue();
    }

    [Test]
    public void JobDtos_RoundTrip()
    {
        RoundTrips<List<JobDto>>("jobs/jobs.json").ShouldBeTrue();
    }

    private static JsonNode Fixture(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;

    private static bool RoundTrips<T>(string name)
    {
        var original = Fixture(name);
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);

        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original);
    }
}
