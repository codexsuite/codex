using System.Net;
using Codex.Contracts.Books;

namespace Codex.WebApi.Tests.Shell;

[TestFixture]
public class LibraryFilterTests : ShellApiFixture
{
    private Guid _windTwo;

    [OneTimeSetUp]
    public async Task InsertShelfAsync()
    {
        var series = await AddSeriesAsync("Zephyr Chronicles");
        await AddBookAsync("Wind One", seriesId: series, number: 1, status: "published", edited: TimeSpan.FromDays(1));
        _windTwo = await AddBookAsync("Wind Two", seriesId: series, number: 2, status: "writing", edited: TimeSpan.FromDays(2));
        await AddBookAsync("Banana", status: "planning", target: 100, words: 20, edited: TimeSpan.FromDays(3));
        await AddBookAsync("apple", subtitle: "A fruit story", status: "draft", target: 100, words: 80, edited: TimeSpan.FromDays(4));
        await AddBookAsync("Old Draft", status: "writing", words: 33, edited: TimeSpan.FromDays(5), archived: true);
    }

    private static string[] Titles(LibraryDto library) => library.Series
        .SelectMany(s => s.Books).Concat(library.Standalone).Select(b => b.Title).Order(StringComparer.Ordinal).ToArray();

    [Test]
    public async Task Get_WhenQMatchesSeries_ShouldReturnItsBooks()
    {
        var library = await Client.GetLibraryAsync("?q=ZEPHYR");

        library.Series.ShouldHaveSingleItem().Books.Select(b => b.Title).ShouldBe(["Wind One", "Wind Two"]);
        library.Standalone.ShouldBeEmpty();
    }

    [Test]
    public async Task Get_WhenQMatchesSubtitle_ShouldReturnTheBook()
    {
        var library = await Client.GetLibraryAsync("?q=fruit");

        Titles(library).ShouldBe(["apple"]);
    }

    [Test]
    public async Task Get_WhenQHasLikeWildcards_ShouldTreatThemAsText()
    {
        Titles(await Client.GetLibraryAsync("?q=%25")).ShouldBeEmpty();
        Titles(await Client.GetLibraryAsync("?q=Wind_One")).ShouldBeEmpty();
    }

    [Test]
    public async Task Get_WhenStatusFilter_ShouldNarrow()
    {
        var library = await Client.GetLibraryAsync("?status=writing");

        library.Series.ShouldHaveSingleItem().Books.ShouldHaveSingleItem().Id.ShouldBe(_windTwo);
        library.Standalone.ShouldBeEmpty();
    }

    [Test]
    public async Task Get_WhenSortTitle_ShouldOrder()
    {
        var library = await Client.GetLibraryAsync("?sort=title");

        library.Standalone.Select(b => b.Title).ShouldBe(["apple", "Banana"]);
    }

    [Test]
    public async Task Get_WhenSortProgress_ShouldPutMostCompleteFirst()
    {
        var library = await Client.GetLibraryAsync("?sort=progress");

        library.Standalone.Select(b => b.Title).ShouldBe(["apple", "Banana"]);
    }

    [Test]
    public async Task Get_WhenSortLastEditedDefault_ShouldPutNewestFirst()
    {
        var library = await Client.GetLibraryAsync();

        library.Series.ShouldHaveSingleItem();
        library.Standalone.Select(b => b.Title).ShouldBe(["Banana", "apple"]);
    }

    [Test]
    public async Task Get_WhenGroupStatus_ShouldOrderStandaloneByStage()
    {
        var library = await Client.GetLibraryAsync("?group=status&sort=title");

        library.Standalone.Select(b => b.Status).ShouldBe(["planning", "draft"]);
    }

    [Test]
    public async Task Get_WhenArchived_ShouldBeExcludedByDefault()
    {
        Titles(await Client.GetLibraryAsync()).ShouldNotContain("Old Draft");
    }

    [Test]
    public async Task Get_WhenStatusArchived_ShouldReturnArchivedBooksWithTheirWords()
    {
        var library = await Client.GetLibraryAsync("?status=archived");

        var book = library.Standalone.ShouldHaveSingleItem();
        book.Title.ShouldBe("Old Draft");
        book.Archived.ShouldBeTrue();
        book.WordCount.ShouldBe(33);
        library.Series.ShouldBeEmpty();
    }

    [Test]
    public async Task Get_WhenSortUnknown_ShouldReturn400ApiError()
    {
        var response = await Client.GetAsync("/api/library?sort=sideways");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
    }

    [Test]
    public async Task Get_WhenGroupOrStatusUnknown_ShouldReturn400ApiError()
    {
        (await (await Client.GetAsync("/api/library?group=colour")).ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await (await Client.GetAsync("/api/library?status=lost")).ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
    }
}
