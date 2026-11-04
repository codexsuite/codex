using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Images the import cannot store: one not sent, one that is not an image. The book is still imported, without them.
[TestFixture]
public class PandocImportImageTests : ApiFixture
{
    private static readonly string NotSent = PlayersGuideCampaign.PlacedImages[0];
    private static readonly string Broken = PlayersGuideCampaign.PlacedImages[6];

    [Test]
    public async Task Import_WhenAnImageIsMissingOrBroken_ShouldLeaveItOutAndListWhy()
    {
        using var form = ImportForms.Pandoc(
            send: PlayersGuideCampaign.PlacedImages.Where(p => p != NotSent),
            replace: path => path == Broken ? "not a png"u8.ToArray() : null);

        using var response = await Client.PostAsync("/api/imports", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        summary.Skipped.ShouldContain(new ImportSkippedDto(NotSent, "Not uploaded with the import. The image was left out."));
        summary.Skipped.ShouldContain(new ImportSkippedDto(Broken, "That file is not a PNG, JPEG or WebP image. The image was left out."));
        await using var db = await OpenDatabaseAsync();
        var ids = new { summary.BookId };
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM asset WHERE book_id = @BookId", ids)).ShouldBe(6);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = @BookId", ids)).ShouldBe(4);
        (await db.ExecuteScalarAsync<Guid?>(
            "SELECT opener_asset_id FROM act WHERE book_id = @BookId AND title = 'Introduction to Velmara'", ids)).ShouldBeNull();
        var fighter = await db.ExecuteScalarAsync<string>(
            "SELECT p.body FROM prose p JOIN scene s ON s.id = p.scene_id JOIN chapter c ON c.id = s.chapter_id WHERE s.book_id = @BookId AND c.title = 'Fighter'", ids);
        fighter!.ShouldNotContain("::figure");
    }
}
