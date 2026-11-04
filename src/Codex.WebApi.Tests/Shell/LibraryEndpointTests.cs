using System.Text.Json.Nodes;
using Codex.Contracts.Books;
using Codex.Core;

namespace Codex.WebApi.Tests.Shell;

[TestFixture]
public class LibraryEndpointTests : ShellApiFixture
{
    private Guid _seriesId;
    private Guid _second;
    private Guid _first;
    private Guid _older;
    private Guid _newer;

    [OneTimeSetUp]
    public async Task InsertShelfAsync()
    {
        _seriesId = await AddSeriesAsync("Alpha Saga", "Three tales.");
        _second = await AddBookAsync("Saga Two", seriesId: _seriesId, number: 2, status: "writing", words: 40, edited: TimeSpan.FromHours(2));
        _first = await AddBookAsync("Saga One", "The start", _seriesId, 1, "published", target: 100, words: 50, edited: TimeSpan.FromDays(3));
        _older = await AddBookAsync("Older Standalone", edited: TimeSpan.FromDays(10));
        _newer = await AddBookAsync("Newer Standalone", status: "draft", target: 10, words: 5, edited: TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task Get_WhenBooksExist_ShouldReturnSeriesAndStandaloneInOrder()
    {
        var library = await Client.GetLibraryAsync();

        var series = library.Series.ShouldHaveSingleItem();
        series.Id.ShouldBe(_seriesId);
        series.Name.ShouldBe("Alpha Saga");
        series.Description.ShouldBe("Three tales.");
        series.BookCount.ShouldBe(2);
        series.PublishedCount.ShouldBe(1);
        series.WordCount.ShouldBe(90);
        series.LastEditedLabel.ShouldBe(DisplayDate.Relative(series.LastEditedAt, TimeProvider.System));
        series.Books.Select(b => b.Id).ShouldBe([_first, _second]);

        var first = series.Books[0];
        first.SeriesName.ShouldBe("Alpha Saga");
        first.SeriesNumber.ShouldBe(1);
        first.Subtitle.ShouldBe("The start");
        first.Status.ShouldBe("published");
        first.WordCount.ShouldBe(50);
        first.TargetWords.ShouldBe(100);
        first.ProgressPct.ShouldBe(50.0m);
        first.CoverInitials.ShouldBe("SO");
        first.CoverPalette.ShouldStartWith("cover-");
        first.LastScreen.ShouldBeNull();
        first.Archived.ShouldBeFalse();
        first.LastEditedLabel.ShouldBe(DisplayDate.Relative(first.LastEditedAt, TimeProvider.System));

        library.Standalone.Select(b => b.Id).ShouldBe([_newer, _older]);
        library.Standalone[1].ProgressPct.ShouldBeNull();
        library.Standalone[0].ProgressPct.ShouldBe(50.0m);
    }

    [Test]
    public async Task Get_WhenResponseRead_ShouldHaveTheFixtureShape()
    {
        var body = JsonNode.Parse(await Client.GetStringAsync("/api/library"))!;
        var fixture = ShellJson.Fixture("library.json");

        ShellJson.PropertyNames(body).ShouldBe(ShellJson.PropertyNames(fixture));
        ShellJson.PropertyNames(body["series"]![0]!).ShouldBe(ShellJson.PropertyNames(fixture["series"]![0]!));
        ShellJson.PropertyNames(body["series"]![0]!["books"]![0]!)
            .ShouldBe(ShellJson.PropertyNames(fixture["series"]![0]!["books"]![0]!));
        ShellJson.PropertyNames(body["standalone"]![0]!).ShouldBe(ShellJson.PropertyNames(fixture["standalone"]![0]!));
    }
}
