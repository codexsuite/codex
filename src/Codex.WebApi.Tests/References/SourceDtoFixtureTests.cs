using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.References;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.References;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/referenceSources.test.ts.
[TestFixture]
public class SourceDtoFixtureTests
{
    [Test]
    public void SourceRowDtos_RoundTrip()
    {
        RoundTrips<List<SourceRowDto>>("references/sources.json").ShouldBeTrue();
    }

    [Test]
    public void SourceCslDtos_RoundTrip()
    {
        RoundTrips<List<SourceCslDto>>("references/sources-csl.json").ShouldBeTrue();
    }

    [Test]
    public void SourceDetailDto_RoundTrips()
    {
        RoundTrips<SourceDetailDto>("references/source-detail.json").ShouldBeTrue();
    }

    [Test]
    public void RecheckResultDto_RoundTrips()
    {
        RoundTrips<RecheckResultDto>("references/recheck-result.json").ShouldBeTrue();
    }

    [Test]
    public void ArchiveSourceResult_RoundTrips()
    {
        RoundTrips<ArchiveSourceResult>("references/archive-result.json").ShouldBeTrue();
    }

    [Test]
    public void LookupSourceResultDto_RoundTrips()
    {
        RoundTrips<LookupSourceResultDto>("references/lookup-result.json").ShouldBeTrue();
    }

    [Test]
    public void AddSourceResult_RoundTrips()
    {
        RoundTrips<AddSourceResult>("references/add-result.json").ShouldBeTrue();
    }

    [Test]
    public void ImportSourcesRequest_RoundTrips()
    {
        RoundTrips<ImportSourcesRequest>("references/import-request.json").ShouldBeTrue();
    }

    [Test]
    public void ImportSourcesResult_RoundTrips()
    {
        RoundTrips<ImportSourcesResult>("references/import-result.json").ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);

        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original);
    }
}
