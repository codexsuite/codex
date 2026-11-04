using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Library;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Library;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/seriesContract.test.ts.
[TestFixture]
public class SeriesDtoFixtureTests
{
    [Test]
    public void SeriesDto_RoundTrips()
    {
        RoundTrips<SeriesDto>("library/series.json").ShouldBeTrue();
    }

    [Test]
    public void PatchSeriesRequest_RoundTrips()
    {
        RoundTrips<PatchSeriesRequest>("library/patch-series-request.json").ShouldBeTrue();
    }

    [Test]
    public void SetSeriesOrderRequest_RoundTrips()
    {
        RoundTrips<SetSeriesOrderRequest>("library/set-series-order-request.json").ShouldBeTrue();
    }

    [Test]
    public void SeriesOrderDto_RoundTrips()
    {
        RoundTrips<SeriesOrderDto>("library/series-order.json").ShouldBeTrue();
    }

    [Test]
    public void CreateBookRequest_RoundTrips()
    {
        RoundTrips<CreateBookRequest>("library/create-book-request.json").ShouldBeTrue();
    }

    [Test]
    public void CreatedBookDto_RoundTrips()
    {
        RoundTrips<CreatedBookDto>("library/created-book.json").ShouldBeTrue();
    }

    [Test]
    public void NextSeriesNumberDto_RoundTrips()
    {
        RoundTrips<NextSeriesNumberDto>("library/next-series-number.json").ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);
        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original);
    }
}
