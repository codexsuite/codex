using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Codex.Contracts.Books;
using Codex.Core;

namespace Codex.WebApi.Tests.Shell;

[TestFixture]
public class BookHeaderEndpointTests : ShellApiFixture
{
    private Guid _seriesId;
    private Guid _bookId;
    private Guid _archivedId;

    [OneTimeSetUp]
    public async Task InsertBooksAsync()
    {
        _seriesId = await AddSeriesAsync("Header Series");
        _bookId = await AddBookAsync("Quiet Harbour", "A subtitle", _seriesId, 3, "revising", target: 1000, words: 250, edited: TimeSpan.FromHours(5));
        _archivedId = await AddBookAsync("Shelved", status: "draft", words: 12, archived: true);
    }

    [Test]
    public async Task Get_WhenExists_ShouldReturnHeader()
    {
        var header = await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_bookId}");

        header.ShouldNotBeNull();
        header.Id.ShouldBe(_bookId);
        header.Title.ShouldBe("Quiet Harbour");
        header.Subtitle.ShouldBe("A subtitle");
        header.AuthorName.ShouldBe("Joel Dickson");
        header.SeriesId.ShouldBe(_seriesId);
        header.SeriesName.ShouldBe("Header Series");
        header.SeriesNumber.ShouldBe(3);
        header.Status.ShouldBe("revising");
        header.CoverInitials.ShouldBe("QH");
        header.CoverPalette.ShouldStartWith("cover-");
        header.WordCount.ShouldBe(250);
        header.TargetWords.ShouldBe(1000);
        header.ReferenceBadgeCount.ShouldBe(0);
        header.ReferenceBadgeTone.ShouldBeNull();
        header.LastEditedLabel.ShouldBe(DisplayDate.Relative(header.LastEditedAt, TimeProvider.System));
        header.Scope.Select(s => (s.Id, s.Kind, s.Title))
            .ShouldBe([(_bookId, "book", "Quiet Harbour"), (_seriesId, "series", "Header Series")]);
    }

    [Test]
    public async Task Get_WhenResponseRead_ShouldHaveTheFixtureShape()
    {
        var body = JsonNode.Parse(await Client.GetStringAsync($"/api/books/{_bookId}"))!;
        var fixture = ShellJson.Fixture("book-header.json");

        ShellJson.PropertyNames(body).ShouldBe(ShellJson.PropertyNames(fixture));
        ShellJson.PropertyNames(body["scope"]![0]!).ShouldBe(ShellJson.PropertyNames(fixture["scope"]![0]!));
    }

    [Test]
    public async Task Get_WhenArchived_ShouldStillReturnHeaderWithWords()
    {
        var header = await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_archivedId}");

        header!.WordCount.ShouldBe(12);
        header.Scope.ShouldHaveSingleItem().Kind.ShouldBe("book");
        header.ReferenceBadgeCount.ShouldBeNull();
    }

    [Test]
    public async Task Get_WhenUnknown_ShouldReturn404ApiError()
    {
        var response = await Client.GetAsync($"/api/books/{Guid.NewGuid()}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test]
    public async Task Get_WhenIdIsASeries_ShouldReturn404ApiError()
    {
        var response = await Client.GetAsync($"/api/books/{_seriesId}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test]
    public async Task Get_WhenIdMalformed_ShouldReturn404ApiError()
    {
        var response = await Client.GetAsync("/api/books/not-a-guid");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }
}
