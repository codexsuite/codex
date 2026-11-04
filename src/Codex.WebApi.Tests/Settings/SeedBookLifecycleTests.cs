using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Jobs;
using Codex.Contracts.Books;
using Codex.Contracts.Settings;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

// One scenario on the seeded Product Engineering book: duplicate, export source, archive and restore.
[TestFixture]
public class SeedBookLifecycleTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;
    private Guid _copy;

    [Test, Order(1)]
    public async Task Duplicate_WhenBookHasReferences_ShouldCopyEverythingButTheReferences()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{Book}/duplicate", new DuplicateBookRequest("  Product Engineering (copy) "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var copy = (await response.Content.ReadFromJsonAsync<DuplicatedBookDto>())!;
        _copy = copy.Id;
        copy.Title.ShouldBe("Product Engineering (copy)");
        await using var db = await OpenDatabaseAsync();
        var counts = await db.QuerySingleAsync<(int References, int Scenes, int Entries, int SourceScenes, int SourceReferences)>(
            """
            SELECT (SELECT count(*) FROM reference WHERE book_id = @copy)::int,
                   (SELECT count(*) FROM scene WHERE book_id = @copy)::int,
                   (SELECT count(*) FROM kb_entry WHERE book_id = @copy)::int,
                   (SELECT count(*) FROM scene WHERE book_id = @source)::int,
                   (SELECT count(*) FROM reference WHERE book_id = @source)::int
            """,
            new { copy = _copy, source = Book });
        counts.References.ShouldBe(0);
        counts.Scenes.ShouldBe(counts.SourceScenes);
        counts.Entries.ShouldBeGreaterThan(0);
        counts.SourceReferences.ShouldBeGreaterThan(0);
    }

    [Test, Order(2)]
    public async Task Duplicate_WhenTitleBlankOrBookUnknown_ShouldAnswer400Or404()
    {
        var blank = await Client.PostAsJsonAsync($"/api/books/{Book}/duplicate", new DuplicateBookRequest("  "));
        var unknown = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/duplicate", new DuplicateBookRequest("Nope"));

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(3)]
    public async Task ExportSource_ShouldQueueASourceJob()
    {
        var response = await Client.PostAsync($"/api/books/{Book}/export-source", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var job = (await response.Content.ReadFromJsonAsync<JobDto>())!;
        (job.BookId, job.Kind, job.State, job.Percent).ShouldBe((Book, "source", "queued", 0));
        var fetched = (await Client.GetFromJsonAsync<JobDto>($"/api/jobs/{job.Id}"))!;
        fetched.State.ShouldBe("queued");
    }

    [Test, Order(4)]
    public async Task Archive_ShouldHideTheBookFromLibraryAndListItAsArchived()
    {
        var response = await Client.PostAsync($"/api/books/{_copy}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<BookSettingsDto>())!.Archived.ShouldBeTrue();
        var live = (await Client.GetFromJsonAsync<LibraryDto>("/api/library"))!;
        live.Standalone.Concat(live.Series.SelectMany(s => s.Books)).ShouldNotContain(b => b.Id == _copy);
        var archived = (await Client.GetFromJsonAsync<LibraryDto>("/api/library?status=archived"))!;
        archived.Standalone.Concat(archived.Series.SelectMany(s => s.Books)).ShouldContain(b => b.Id == _copy);
    }

    [Test, Order(5)]
    public async Task Archive_WhenAlreadyArchived_ShouldAnswer409()
    {
        var response = await Client.PostAsync($"/api/books/{_copy}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(6)]
    public async Task Restore_ShouldBringTheBookBackThenConflictAgainAConflict()
    {
        var response = await Client.PostAsync($"/api/books/{_copy}/restore", null);
        var again = await Client.PostAsync($"/api/books/{_copy}/restore", null);

        (await response.Content.ReadFromJsonAsync<BookSettingsDto>())!.Archived.ShouldBeFalse();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var live = (await Client.GetFromJsonAsync<LibraryDto>("/api/library"))!;
        live.Standalone.Concat(live.Series.SelectMany(s => s.Books)).ShouldContain(b => b.Id == _copy);
    }

    [Test, Order(7)]
    public async Task Archive_WhenBookUnknown_ShouldAnswer404()
    {
        var response = await Client.PostAsync($"/api/books/{Guid.NewGuid()}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
