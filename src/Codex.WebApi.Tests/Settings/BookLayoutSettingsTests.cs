using System.Net.Http.Json;
using Codex.Contracts.Settings;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class BookLayoutSettingsTests : ApiFixture
{
    [Test]
    public async Task Layout_WhenBookHasATemplate_ShouldAnswerItsPinnedVersionTrimAndTrims()
    {
        await using var db = await OpenDatabaseAsync();
        var book = await TestRows.ShelfBookAsync(db, "Laid out", null, null, null, "writing", 1000, 0, null, false);
        await db.ExecuteAsync(
            """
            INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size)
            SELECT @book, t.id, v.id, '5.5x8.5' FROM layout_template t JOIN layout_template_version v ON v.template_id = t.id
            WHERE t.slug = 'novel-6x9' AND t.owner_id IS NULL;
            INSERT INTO book_layout (book_id, template_id) SELECT @book, template_id FROM book_layout_template WHERE book_id = @book;
            """,
            new { book });

        var layout = (await Client.GetFromJsonAsync<LayoutSettingsDto>($"/api/books/{book}/settings/layout"))!;

        layout.Template.ShouldNotBeNull();
        (layout.Template.Name, layout.Template.PinnedVersion, layout.Template.TrimSize, layout.Template.LatestVersion)
            .ShouldBe(("Novel (6 x 9)", "1.0.0", "5.5x8.5", null));
        layout.Template.Trims.ShouldBe(["6x9", "5.5x8.5"]);
        layout.RiskCount.ShouldBe(0);
    }
}
