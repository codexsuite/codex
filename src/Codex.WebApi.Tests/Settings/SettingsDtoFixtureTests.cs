using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Settings;
using Codex.TestSupport;

namespace Codex.WebApi.Tests.Settings;

// The same JSON files are type-checked and round-tripped by src/Codex.Core.Client/src/api/settingsContract.test.ts.
[TestFixture]
public class SettingsDtoFixtureTests
{
    [Test]
    public void BookSettingsDto_RoundTrips()
    {
        RoundTrips<BookSettingsDto>("settings/book-settings.json").ShouldBeTrue();
    }

    [Test]
    public void PatchBookSettingsRequest_RoundTrips()
    {
        RoundTrips<PatchBookSettingsRequest>("settings/patch-book-settings-request.json").ShouldBeTrue();
    }

    [Test]
    public void SetSeriesNumberRequest_RoundTrips()
    {
        RoundTrips<SetSeriesNumberRequest>("settings/set-series-number-request.json").ShouldBeTrue();
    }

    [Test]
    public void SeriesNumberChangeDto_RoundTrips()
    {
        RoundTrips<SeriesNumberChangeDto>("settings/series-number-change.json").ShouldBeTrue();
    }

    [Test]
    public void SeriesMoveRequest_RoundTrips()
    {
        RoundTrips<SeriesMoveRequest>("settings/series-move-request.json").ShouldBeTrue();
    }

    [Test]
    public void SeriesMovePreviewDto_RoundTrips()
    {
        RoundTrips<SeriesMovePreviewDto>("settings/series-move-preview.json").ShouldBeTrue();
    }

    [Test]
    public void PublicationSettingsDto_RoundTrips()
    {
        RoundTrips<PublicationSettingsDto>("settings/publication-settings.json").ShouldBeTrue();
    }

    [Test]
    public void PatchPublicationSettingsRequest_RoundTrips()
    {
        RoundTrips<PatchPublicationSettingsRequest>("settings/patch-publication-settings-request.json").ShouldBeTrue();
    }

    [Test]
    public void RulesImpactDto_RoundTrips()
    {
        RoundTrips<RulesImpactDto>("settings/rules-impact.json").ShouldBeTrue();
    }

    [Test]
    public void LayoutSettingsDto_RoundTrips()
    {
        RoundTrips<LayoutSettingsDto>("settings/layout-settings.json").ShouldBeTrue();
    }

    [Test]
    public void DuplicatedBookDto_RoundTrips()
    {
        RoundTrips<DuplicatedBookDto>("settings/duplicated-book.json").ShouldBeTrue();
    }

    private static bool RoundTrips<T>(string name)
    {
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", name)))!;
        var model = original.Deserialize<T>(JsonSerializerOptions.Web);
        return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(model, JsonSerializerOptions.Web), original);
    }
}
