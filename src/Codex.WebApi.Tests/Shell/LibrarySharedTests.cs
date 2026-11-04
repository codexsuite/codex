using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Shell;

// The Library and the book header as a collaborator (#80). JD shares the whole "Whole Saga" series with Ana to comment,
// one book of "Partial Saga" to edit and the standalone "Shared Alone" to view; Ana also owns a book. Ana sees each
// shared book with her role and JD as its owner, Partial Saga with only her book (and a roll-up of just that one),
// and no archived shelf of JD's.
[TestFixture]
public class LibrarySharedTests : ShellApiFixture
{
    private static readonly Guid Ana = new("0199c0de-0000-7000-8000-0000000000d1");
    private Guid _wholeSaga;
    private Guid _a1;
    private Guid _a2;
    private Guid _partialSaga;
    private Guid _b2;
    private Guid _sharedAlone;
    private Guid _private;
    private Guid _anasOwn;
    private HttpClient _ana = null!;

    [OneTimeSetUp]
    public async Task InsertShelfAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "ana@example.test", "Ana Kowalczyk", id: Ana);
        _wholeSaga = await AddSeriesAsync("Whole Saga");
        _a1 = await AddBookAsync("Whole One", seriesId: _wholeSaga, number: 1, status: "published", words: 10, edited: TimeSpan.FromDays(2));
        _a2 = await AddBookAsync("Whole Two", seriesId: _wholeSaga, number: 2, words: 20, edited: TimeSpan.FromDays(3));
        await AddBookAsync("Whole Archived", seriesId: _wholeSaga, number: 3, words: 5, archived: true);
        _partialSaga = await AddSeriesAsync("Partial Saga");
        await AddBookAsync("Partial One", seriesId: _partialSaga, number: 1, status: "published", words: 100, edited: TimeSpan.FromMinutes(5));
        _b2 = await AddBookAsync("Partial Two", seriesId: _partialSaga, number: 2, status: "writing", words: 40, edited: TimeSpan.FromDays(4));
        await AddBookAsync("Partial Three", seriesId: _partialSaga, number: 3, words: 70, edited: TimeSpan.FromMinutes(1));
        _sharedAlone = await AddBookAsync("Shared Alone", edited: TimeSpan.FromDays(1));
        _private = await AddBookAsync("Private", edited: TimeSpan.FromHours(1));

        await using var db = await OpenDatabaseAsync();
        _anasOwn = await db.ExecuteScalarAsync<Guid>("INSERT INTO book (owner_id, title) VALUES (@Ana, 'Anas Own') RETURNING id", new { Ana });
        await db.ExecuteAsync("UPDATE book SET last_edited_at = now() - interval '6 days' WHERE id = @id", new { id = _anasOwn });
        await TestRows.GrantAsync(db, _wholeSaga, Ana, "comment");
        await TestRows.GrantAsync(db, _b2, Ana, "write");
        await TestRows.GrantAsync(db, _sharedAlone, Ana, "view");
        _ana = Host.CreateClient().ActingAs(Ana);
    }

    [OneTimeTearDown]
    public void DisposeClient() => _ana.Dispose();

    [Test]
    public async Task Get_WhenBooksAreSharedWithTheCaller_ShouldListThemWithRoleAndOwner()
    {
        var library = await _ana.GetLibraryAsync();

        library.Series.Select(s => s.Id).Order().ShouldBe(new[] { _wholeSaga, _partialSaga }.Order());
        var whole = library.Series.Single(s => s.Id == _wholeSaga);
        whole.Books.Select(b => (b.Id, b.Role)).ShouldBe([(_a1, "comment"), (_a2, "comment")]);
        whole.PartlyShared.ShouldBeFalse();
        whole.Books.ShouldAllBe(b => b.Owner == new BookOwnerDto(SeedIds.Users.Jd, "JD"));
        library.Standalone.Select(b => (b.Id, b.Role)).ShouldBe([(_sharedAlone, "view"), (_anasOwn, "owner")]);
        library.Standalone[1].Owner.ShouldBe(new BookOwnerDto(Ana, "Ana Kowalczyk"));
    }

    [Test]
    public async Task Get_WhenOnlyOneBookOfASeriesIsShared_ShouldShowItAloneUnderTheSeries()
    {
        var library = await _ana.GetLibraryAsync();

        var partial = library.Series.Single(s => s.Id == _partialSaga);
        partial.Name.ShouldBe("Partial Saga");
        partial.Books.Select(b => (b.Id, b.Role)).ShouldBe([(_b2, "write")]);
        (partial.BookCount, partial.PublishedCount, partial.WordCount).ShouldBe((1, 0, 40));
        partial.PartlyShared.ShouldBeTrue();
    }

    [Test]
    public async Task Get_WhenTheOwnerAsks_ShouldStillCountEveryLiveBook()
    {
        var library = await Client.GetLibraryAsync();

        var partial = library.Series.Single(s => s.Id == _partialSaga);
        (partial.BookCount, partial.PublishedCount, partial.WordCount).ShouldBe((3, 1, 210));
        library.Series.Single(s => s.Id == _wholeSaga).BookCount.ShouldBe(2);
        library.Series.ShouldAllBe(s => !s.PartlyShared);
        library.Series.SelectMany(s => s.Books).Concat(library.Standalone).ShouldAllBe(b => b.Role == "owner");
        library.Standalone.Select(b => b.Id).ShouldBe([_private, _sharedAlone]);
    }

    [Test]
    public async Task Get_WhenTheArchivedShelfIsAsked_ShouldListOnlyTheCallersOwnBooks()
    {
        var library = await _ana.GetLibraryAsync("?status=archived");

        library.Series.SelectMany(s => s.Books).Concat(library.Standalone).ShouldBeEmpty();
    }

    [Test]
    public async Task GetBook_WhenTheBookIsShared_ShouldCarryTheRoleAndOwner()
    {
        var header = await _ana.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_b2}");

        header!.Role.ShouldBe("write");
        header.Owner.ShouldBe(new BookOwnerDto(SeedIds.Users.Jd, "JD"));
        header.SeriesName.ShouldBe("Partial Saga");
    }

    [Test]
    public async Task GetBook_WhenTheBookIsNotShared_ShouldBeNotFound()
    {
        var response = await _ana.GetAsync($"/api/books/{_private}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
