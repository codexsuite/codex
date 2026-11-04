using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// The access rule over content (#80): Ana has write on the whole Forsaken Gods series (seed 09_sharing), so she edits
// its books' prose, the series' shared knowledge base and uploads images to its books, as JD would.
[TestFixture]
public class ContentAccessSeriesWriterTests : ApiFixture
{
    private static readonly Guid Scene = SeedIds.Scenes.PlayersGuideFigureScene;

    protected override TestUser SignedInAs => TestUser.Of(SeedIds.Users.Ana);

    [Test]
    public async Task SaveProse_WhenTheSeriesIsSharedToWrite_ShouldSaveAsTheWriter()
    {
        var before = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose"))!;

        using var response = await Client.PutAsJsonAsync(
            $"/api/scenes/{Scene}/prose", new SaveProseRequest(before.Body + "\n\nAna adds a line.\n", before.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose"))!;
        after.Body.ShouldEndWith("Ana adds a line.\n");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT updated_by FROM prose WHERE scene_id = @Scene", new { Scene })).ShouldBe(SeedIds.Users.Ana);
    }

    [Test]
    public async Task EditSeriesKbEntry_WhenTheSeriesIsSharedToWrite_ShouldSaveTheEdit()
    {
        using var response = await Client.PatchAsJsonAsync(
            $"/api/kb/entries/{SeedIds.KbEntries.Gravetouch}", new PatchKbEntryRequest(null, "Edited by Ana.", null, null, null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<KbEntryDto>())!.Summary.ShouldBe("Edited by Ana.");
    }

    [Test]
    public async Task UploadImage_WhenTheSeriesIsSharedToWrite_ShouldStoreItInTheBookWithAnaAsTheUploader()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Print.TestImages.Photo(40, 30));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "plate.png");

        using var response = await Client.PostAsync($"/api/books/{SeedIds.Books.PlayersGuide}/assets", form);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var asset = (await response.Content.ReadFromJsonAsync<AssetDto>())!;
        asset.BookId.ShouldBe(SeedIds.Books.PlayersGuide);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT owner_id FROM asset WHERE id = @Id", new { asset.Id })).ShouldBe(SeedIds.Users.Ana);
        // JD, the book's owner, reads the image Ana uploaded through the book.
        using var jd = Host.CreateClient().ActingAs(SeedIds.Users.Jd);
        (await jd.GetAsync($"/api/assets/{asset.Id}/info")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
