using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The layout half of the seed (#16 Part 1 and D3): the Players Guide's low-dpi figure and raw-LaTeX block and the
// three built-in templates. Read-only against one clone.
[TestFixture]
public class SeedLayoutTests
{
    private ClonedDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    [Test]
    public async Task PlayersGuide_ShouldHaveLowDpiFigureAndRawLatex()
    {
        await using var connection = await _database.OpenAsync();

        var asset = await connection.QuerySingleAsync<(Guid Book, string Kind, int Width, int Height, string Dpi)>(
            "SELECT book_id, kind, width_px, height_px, meta ->> 'dpi' FROM asset WHERE id = @id", new { id = SeedIds.Assets.PlayersGuideFigure });
        var figure = await connection.QuerySingleAsync<(Guid Scene, Guid Asset, bool Archived)>(
            "SELECT scene_id, asset_id, archived_at IS NOT NULL FROM figure WHERE id = @id", new { id = SeedIds.Figures.PlayersGuideFigure });
        var figureBody = await connection.QuerySingleAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.PlayersGuideFigureScene });
        var latexBody = await connection.QuerySingleAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.PlayersGuideLatexScene });
        var latexWords = await connection.ExecuteScalarAsync<int>(
            "SELECT word_count FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.PlayersGuideLatexScene });

        asset.ShouldBe((SeedIds.Books.PlayersGuide, "image", 480, 320, "72"));
        (asset.Width / 3.5).ShouldBeLessThan(300); // a 3.5 inch column: well under 300 dpi
        figure.ShouldBe((SeedIds.Scenes.PlayersGuideFigureScene, SeedIds.Assets.PlayersGuideFigure, false));
        figureBody.ShouldContain($"\n::figure{{id=\"{SeedIds.Figures.PlayersGuideFigure}\"}}\n");
        latexBody.ShouldContain("\n```{=latex}\n");
        latexBody.ShouldContain("\\begin{tabular}{ll}");
        latexBody.ShouldEndWith("\n```\n");
        latexWords.ShouldBe(await connection.ExecuteScalarAsync<int>("SELECT markdown_word_count(@b)", new { b = latexBody.Split("```{=latex}")[0] }));
    }

    [Test]
    public async Task PlayersGuide_ShouldKeepItsRecencyAfterTheEmbedWrites()
    {
        await using var connection = await _database.OpenAsync();

        // Seed timestamps are stamped when the shared template is built, so only the gap between the two books is stable.
        var gapMinutes = await connection.ExecuteScalarAsync<double>(
            """
            SELECT extract(epoch FROM pe.last_edited_at - pg.last_edited_at) / 60
            FROM book pe, book pg WHERE pe.id = @productEngineering AND pg.id = @playersGuide
            """,
            new { productEngineering = SeedIds.Books.ProductEngineering, playersGuide = SeedIds.Books.PlayersGuide });

        gapMinutes.ShouldBeInRange(115, 125);
    }

    [Test]
    public async Task LayoutTemplates_ShouldBeThreeBuiltIns()
    {
        await using var connection = await _database.OpenAsync();

        var templates = (await connection.QueryAsync<(string Slug, string Name, bool Published)>(
            """
            SELECT t.slug, t.name, EXISTS (SELECT 1 FROM layout_template_version v WHERE v.template_id = t.id AND v.published_at IS NOT NULL)
            FROM layout_template t WHERE t.owner_id IS NULL ORDER BY t.slug
            """)).ToList();

        templates.ShouldBe(
        [
            ("fantasy-rpg", "Fantasy RPG (5e style)", true),
            ("manuscript", "Manuscript", true),
            ("novel-6x9", "Novel (6 x 9)", true),
        ]);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM layout_template")).ShouldBe(3);
    }
}
