using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

// J26 on the seeded books: publish Undertow (snapshot job), refuse the empty Northbound, then a second edition of
// Product Engineering carries the sources (cleared of sign-offs) with its markers re-pointed, and plain Duplicate does not.
[TestFixture]
public class PublishLifecycleTests : ApiFixture
{
    private static readonly Guid Undertow = SeedIds.Books.Undertow;
    private static readonly Guid Northbound = SeedIds.Books.Northbound;
    private static readonly Guid Product = SeedIds.Books.ProductEngineering;

    [Test, Order(1)]
    public async Task Get_BeforePublishing_ShouldReadWordsChaptersAndNoSnapshots()
    {
        var state = (await Client.GetFromJsonAsync<PublishStateDto>($"/api/books/{Undertow}/publish"))!;

        state.Status.ShouldBe("writing");
        state.ChapterCount.ShouldBeGreaterThan(0);
        state.WordCount.ShouldBeGreaterThan(0);
        state.AtRiskCount.ShouldBe(0);
        state.Snapshots.ShouldBeEmpty();
    }

    [Test, Order(2)]
    public async Task Publish_ShouldSetStatusAndQueueOneSnapshotJob()
    {
        var before = (await Client.GetFromJsonAsync<PublishStateDto>($"/api/books/{Undertow}/publish"))!;

        var response = await Client.PostAsync($"/api/books/{Undertow}/publish", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = (await response.Content.ReadFromJsonAsync<PublishStateDto>())!;
        state.Status.ShouldBe("published");
        var snapshot = state.Snapshots.ShouldHaveSingleItem();
        (snapshot.Label, snapshot.Words, snapshot.State).ShouldBe(("Published", before.WordCount, "queued"));
        snapshot.FileUrl.ShouldBeNull();
        await using var db = await OpenDatabaseAsync();
        var job = await db.QuerySingleAsync<(string Kind, bool Snapshot)>(
            "SELECT kind, (settings ->> 'snapshot')::boolean FROM export_job WHERE book_id = @Undertow", new { Undertow });
        job.ShouldBe(("source", true));
    }

    [Test, Order(3)]
    public async Task Publish_WhenAlreadyPublished_ShouldTakeAnotherSnapshot()
    {
        var state = (await (await Client.PostAsync($"/api/books/{Undertow}/publish", null)).Content.ReadFromJsonAsync<PublishStateDto>())!;

        state.Snapshots.Count.ShouldBe(2);
    }

    [Test, Order(4)]
    public async Task Library_WhenEditedAfterTheSnapshot_ShouldFlagEditedSincePublication()
    {
        await using var db = await OpenDatabaseAsync();
        (await LibraryBookAsync(Undertow)).EditedSincePublication.ShouldBeFalse();

        await db.ExecuteAsync("UPDATE book SET last_edited_at = now() + interval '1 minute' WHERE id = @Undertow", new { Undertow });

        (await LibraryBookAsync(Undertow)).EditedSincePublication.ShouldBeTrue();
    }

    [Test, Order(5)]
    public async Task Publish_WhenBookHasNoChapter_ShouldBeBlocked()
    {
        var response = await Client.PostAsync($"/api/books/{Northbound}/publish", null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = await response.ReadApiErrorAsync();
        (error.Code, error.Error).ShouldBe((ApiErrorCodes.Blocked, "Nothing to publish. Add a chapter first."));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM book WHERE id = @Northbound", new { Northbound })).ShouldBe("planning");
        (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM export_job WHERE book_id = @Northbound", new { Northbound })).ShouldBe(0);
    }

    [Test, Order(6)]
    public async Task Publish_WhenUnknownBook_ShouldAnswer404()
    {
        (await Client.PostAsync($"/api/books/{Guid.NewGuid()}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/books/{Guid.NewGuid()}/publish")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(7)]
    public async Task Publish_WhenSourcesAreAtRisk_ShouldStillPublishAndReportTheRisks()
    {
        var before = (await Client.GetFromJsonAsync<PublishStateDto>($"/api/books/{Product}/publish"))!;
        before.AtRiskCount.ShouldBe(3);

        var state = (await (await Client.PostAsync($"/api/books/{Product}/publish", null)).Content.ReadFromJsonAsync<PublishStateDto>())!;

        state.Status.ShouldBe("published");
    }

    [Test, Order(8)]
    public async Task NewEdition_ShouldCopyTheSourcesWithoutSignOffsAndRepointTheMarkers()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{Product}/new-edition", new DuplicateBookRequest("Product Engineering (2nd edition)"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edition = (await response.Content.ReadFromJsonAsync<DuplicatedBookDto>())!;
        await using var db = await OpenDatabaseAsync();
        var facts = await db.QuerySingleAsync<(int Sources, int SourcesOld, int SignedOff, int Requests, int Uses, int UsesOld, int Orphans, int Words, int WordsOld, string Status, Guid? CopiedFrom)>(
            """
            SELECT (SELECT count(*) FROM reference WHERE book_id = @e)::int,
                   (SELECT count(*) FROM reference WHERE book_id = @p)::int,
                   (SELECT count(*) FROM reference WHERE book_id = @e AND signed_off_at IS NOT NULL)::int,
                   (SELECT count(*) FROM permission_request q JOIN reference r ON r.id = q.reference_id WHERE r.book_id = @e)::int,
                   (SELECT count(*) FROM reference_use WHERE book_id = @e)::int,
                   (SELECT count(*) FROM reference_use WHERE book_id = @p AND generation_id IS NULL)::int,
                   (SELECT count(*) FROM prose pr JOIN scene s ON s.id = pr.scene_id
                    CROSS JOIN LATERAL regexp_matches(pr.body, '\(ref:([0-9a-fA-F-]{36})\)', 'g') AS m
                    WHERE s.book_id = @e AND NOT EXISTS (SELECT 1 FROM reference_use u WHERE u.id = m[1]::uuid AND u.book_id = @e))::int,
                   (SELECT sum(word_count) FROM scene_stats WHERE book_id = @e)::int,
                   (SELECT sum(word_count) FROM scene_stats WHERE book_id = @p)::int,
                   (SELECT status FROM book WHERE id = @e),
                   (SELECT copied_from_id FROM book WHERE id = @e)
            """,
            new { e = edition.Id, p = Product });
        facts.Sources.ShouldBe(facts.SourcesOld);
        facts.SignedOff.ShouldBe(0);
        facts.Requests.ShouldBe(0);
        facts.Uses.ShouldBe(facts.UsesOld);
        facts.Orphans.ShouldBe(0);
        facts.Words.ShouldBe(facts.WordsOld);
        facts.Status.ShouldBe("planning");
        facts.CopiedFrom.ShouldBe(Product);
        var summary = (await Client.GetFromJsonAsync<Codex.Contracts.References.ClearanceSummaryDto>($"/api/books/{edition.Id}/references/summary"))!;
        summary.Cleared.ShouldBe(0);
        summary.Risks.ShouldBe(3);
    }

    [Test, Order(9)]
    public async Task NewEdition_WhenTitleBlankOrBookUnknown_ShouldAnswer400Or404()
    {
        (await Client.PostAsJsonAsync($"/api/books/{Product}/new-edition", new DuplicateBookRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/new-edition", new DuplicateBookRequest("x"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Codex.Contracts.Books.LibraryBookDto> LibraryBookAsync(Guid id)
    {
        var library = (await Client.GetFromJsonAsync<Codex.Contracts.Books.LibraryDto>("/api/library"))!;
        return library.Standalone.Concat(library.Series.SelectMany(s => s.Books)).Single(b => b.Id == id);
    }
}
