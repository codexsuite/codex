using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// The #10 layout at full size (230 files, generated): one import, then Dismiss hides the banner.
[TestFixture]
public class ImportVolumeTests : ApiFixture
{
    private ImportSummaryDto _summary = null!;

    private object Ids => new { _summary.BookId, _summary.ImportId };

    [Test, Order(1)]
    public async Task Import_WhenFullSizeLayout_ShouldCreateEverythingInOneGo()
    {
        var files = ImportForms.FullSizeLayout();
        files.Count.ShouldBe(230);

        var response = await Client.PostAsync("/api/imports", ImportForms.Form(files));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        (_summary.FileName, _summary.Acts, _summary.Chapters, _summary.Scenes, _summary.Entries).ShouldBe(("book", 6, 30, 90, 7));
        _summary.Skipped.Count.ShouldBe(76);
        _summary.Skipped.ShouldAllBe(s => s.Reason == "Outside the manuscript folder");
        await using var db = await OpenDatabaseAsync();
        var counts = await db.QuerySingleAsync<(int Beats, int Prose, int Revisions, int Links)>(
            """
            SELECT (SELECT count(*)::int FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = @BookId AND b.archived_at IS NULL),
                   (SELECT count(*)::int FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId AND p.body LIKE '%six words.%'),
                   (SELECT count(*)::int FROM prose_revision r JOIN scene s ON s.id = r.scene_id WHERE s.book_id = @BookId AND r.source = 'import'),
                   (SELECT count(*)::int FROM scene_kb k JOIN scene s ON s.id = k.scene_id WHERE s.book_id = @BookId)
            """, Ids);
        counts.ShouldBe((180, 25, 90, 180));
    }

    [Test, Order(2)]
    public async Task Dismiss_WhenBannerShowing_ShouldHideIt()
    {
        (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!.Banner.ShouldNotBeNull();

        var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/dismiss", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!.Banner.ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task Dismiss_WhenImportUnknown_ShouldAnswer404()
    {
        var response = await Client.PostAsync($"/api/imports/{Guid.NewGuid()}/dismiss", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }
}
