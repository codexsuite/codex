using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Print;

// The figure commands (#65) on the Players Guide's raw-LaTeX scene: a figure is created with its ::figure line in one
// write, refused when the version is stale or the image is outside the book, changed, then removed, which archives it.
[TestFixture]
public class FigureCommandTests : ApiFixture
{
    private static readonly Guid Scene = SeedIds.Scenes.PlayersGuideLatexScene;
    private static readonly Guid Asset = SeedIds.Assets.PlayersGuideFigure;
    private static readonly Guid Figure = Guid.CreateVersion7();

    [Test, Order(1)]
    public async Task Create_WhenTheVersionIsStale_ShouldConflictAndWriteNothing()
    {
        var prose = await ProseAsync();

        using var response = await Client.PostAsJsonAsync($"/api/scenes/{Scene}/figures",
            new CreateFigureRequest(Figure, Asset, WithLine(prose.Body), prose.Version - 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await ProseAsync()).ShouldBe(prose);
        (await FigureExistsAsync()).ShouldBeFalse();
    }

    [Test, Order(2)]
    public async Task Create_WhenTheImageIsOutsideTheBook_ShouldBeValidation()
    {
        var prose = await ProseAsync();
        await using var db = await OpenDatabaseAsync();
        var otherBooksImage = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO asset (owner_id, book_id, kind, filename, mime, bytes, width_px, height_px, storage_key)
            VALUES (@owner, @book, 'image', 'x.png', 'image/png', 1, 10, 10, 'x') RETURNING id
            """, new { owner = SeedIds.Users.Jd, book = SeedIds.Books.ProductEngineering });

        using var response = await Client.PostAsJsonAsync($"/api/scenes/{Scene}/figures",
            new CreateFigureRequest(Figure, otherBooksImage, WithLine(prose.Body), prose.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await FigureExistsAsync()).ShouldBeFalse();
    }

    [Test, Order(3)]
    public async Task Create_WhenThePlacementIsUnknown_ShouldBeValidation()
    {
        var prose = await ProseAsync();

        using var response = await Client.PostAsJsonAsync($"/api/scenes/{Scene}/figures",
            new CreateFigureRequest(Figure, Asset, WithLine(prose.Body), prose.Version, Placement: "sidebar"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(4)]
    public async Task Create_WhenValid_ShouldWriteTheRowAndTheLineTogether()
    {
        var prose = await ProseAsync();

        using var response = await Client.PostAsJsonAsync($"/api/scenes/{Scene}/figures",
            new CreateFigureRequest(Figure, Asset, WithLine(prose.Body), prose.Version, "page", " A map. ", "The map"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = (await response.Content.ReadFromJsonAsync<CreateFigureResult>())!;
        created.Version.ShouldBe(prose.Version + 1);
        (await ProseAsync()).Body.ShouldBe(WithLine(prose.Body));
        var figure = (await Client.GetFromJsonAsync<FigureDto>($"/api/figures/{Figure}"))!;
        (figure.SceneId, figure.AssetId, figure.Placement, figure.Caption, figure.AltText).ShouldBe((Scene, Asset, "page", "A map.", "The map"));
    }

    [Test, Order(5)]
    public async Task Patch_WhenCaptionIsEmpty_ShouldClearItAndKeepTheRest()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/figures/{Figure}", new PatchFigureRequest(Placement: "float-top", Caption: ""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var figure = (await response.Content.ReadFromJsonAsync<FigureDto>())!;
        (figure.Placement, figure.Caption, figure.AltText).ShouldBe(("float-top", null, "The map"));
    }

    [Test, Order(6)]
    public async Task Remove_WhenTheVersionIsCurrent_ShouldTakeTheLineOutAndArchiveTheFigure()
    {
        var prose = await ProseAsync();

        using var response = await Client.PostAsJsonAsync($"/api/figures/{Figure}/remove", new RemoveFigureRequest(prose.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ProseAsync()).Body.ShouldNotContain(Figure.ToString());
        using var gone = await Client.GetAsync($"/api/figures/{Figure}");
        (await gone.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private static string WithLine(string body) => body.TrimEnd('\n') + $"\n\n::figure{{id=\"{Figure}\"}}\n";

    private async Task<(string Body, int Version)> ProseAsync()
    {
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose"))!;
        return (prose.Body, prose.Version);
    }

    private async Task<bool> FigureExistsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM figure WHERE id = @Figure)", new { Figure });
    }
}
