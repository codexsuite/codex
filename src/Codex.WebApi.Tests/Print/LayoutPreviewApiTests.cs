using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// The Layout view's endpoints on the seed's Players Guide (a low-dpi figure and a raw-LaTeX block in chapter 1).
// Needs pandoc on PATH, as CI installs it.
[TestFixture]
public class LayoutPreviewApiTests : ApiFixture
{
    private static readonly string Base = $"/api/books/{SeedIds.Books.PlayersGuide}/layout";

    private static LayoutPreviewRequest Chapter(string? template = "fantasy-rpg", string? trim = null, Dictionary<string, JsonElement>? options = null) =>
        new("chapter", SeedIds.Chapters.PlayersGuideCh1, template, trim, options);

    private static async Task<string> CodeAsync(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        return (await response.ReadApiErrorAsync(status)).Code;
    }

    [Test, Order(1)]
    public async Task Get_WhenTheBookHasChosenNothing_ShouldOfferTheThreeTemplatesAndDefaultToTheNovel()
    {
        var state = (await Client.GetFromJsonAsync<LayoutStateDto>(Base))!;

        state.Template.ShouldBe("novel-6x9");
        state.Trim.ShouldBe("6x9");
        state.Templates.Select(t => t.Slug).Order().ShouldBe(["fantasy-rpg", "manuscript", "novel-6x9"]);
        var novel = state.Templates.Single(t => t.Slug == "novel-6x9");
        novel.Trims.ShouldBe(["6x9", "5.5x8.5"]);
        novel.Options.Select(o => (o.Key, o.Type, o.Value.GetBoolean())).ShouldBe([("drop-caps", "boolean", false), ("toc", "boolean", false)], ignoreOrder: true);
        state.Templates.Single(t => t.Slug == "fantasy-rpg").Columns.ShouldBe(2);
    }

    [Test, Order(2)]
    public async Task Preview_WhenAChapterHasAFigureAndRawLatex_ShouldReturnPagesHtmlCssFlagsAndTheLowDpiFigure()
    {
        using var response = await Client.PostAsJsonAsync(Base + "/preview", Chapter());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<LayoutPreviewDto>())!;

        preview.Template.ShouldBe("fantasy-rpg");
        preview.Trim.ShouldBe("us-letter");
        preview.Css.ShouldNotBeNullOrWhiteSpace();
        preview.Notices.ShouldBeEmpty();
        preview.Html.ShouldContain("codex-scene");
        preview.Html.ShouldContain($"data-scene=\"{SeedIds.Scenes.PlayersGuideFigureScene}\"");
        preview.Html.ShouldContain("data-label=\"Ch 1 · Sc ");
        preview.Flags.ShouldContain(f => f.Kind == "raw_latex" && f.SceneId == SeedIds.Scenes.PlayersGuideLatexScene);
        var figure = preview.Figures.ShouldHaveSingleItem();
        figure.Status.ShouldBe("low");
        (figure.WidthPx, figure.HeightPx, figure.Placement).ShouldBe((480, 320, "column"));
        figure.EffectiveDpi.ShouldNotBeNull().ShouldBeInRange(100, 299);
        preview.ContentHash.ShouldNotBeNullOrEmpty();
    }

    [Test, Order(3)]
    public async Task Preview_WhenTheTrimOrOptionsChange_ShouldReflowTheSameTextWithAnotherHash()
    {
        var first = (await (await Client.PostAsJsonAsync(Base + "/preview", Chapter())).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;
        var a4 = (await (await Client.PostAsJsonAsync(Base + "/preview", Chapter(trim: "a4"))).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;

        a4.Trim.ShouldBe("a4");
        a4.ContentHash.ShouldNotBe(first.ContentHash);
        a4.Html.ShouldBe(first.Html);
    }

    [Test, Order(4)]
    public async Task Preview_WhenTheScopeIsASceneOrTheBook_ShouldRenderThatMuch()
    {
        var scene = (await (await Client.PostAsJsonAsync(Base + "/preview",
            new LayoutPreviewRequest("scene", SeedIds.Scenes.PlayersGuideLatexScene, "fantasy-rpg"))).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;
        scene.Html.ShouldContain($"data-scene=\"{SeedIds.Scenes.PlayersGuideLatexScene}\"");
        scene.Html.ShouldNotContain($"data-scene=\"{SeedIds.Scenes.PlayersGuideFigureScene}\"");

        using var book = await Client.PostAsJsonAsync(Base + "/preview", new LayoutPreviewRequest("book"));
        book.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test, Order(5)]
    public async Task Preview_WhenTheRequestIsWrong_ShouldAnswerAnApiError()
    {
        (await CodeAsync(Client.PostAsJsonAsync(Base + "/preview", new LayoutPreviewRequest("page")), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PostAsJsonAsync(Base + "/preview", new LayoutPreviewRequest("chapter")), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PostAsJsonAsync(Base + "/preview", Chapter(trim: "6x9")), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PostAsJsonAsync(Base + "/preview", Chapter(template: "nope")), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PostAsJsonAsync(Base + "/preview", new LayoutPreviewRequest("chapter", Guid.NewGuid())), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/layout/preview", Chapter()), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.GetAsync($"/api/books/{Guid.NewGuid()}/layout"), HttpStatusCode.NotFound)).ShouldBe("not_found");
    }

    [Test, Order(6)]
    public async Task Select_WhenTheBookPicksATemplateTrimAndOption_ShouldKeepItForThePreviewAndTheSettings()
    {
        using var response = await Client.PutAsJsonAsync(Base, new LayoutSelectionRequest(
            "novel-6x9", "5.5x8.5", new Dictionary<string, JsonElement> { ["drop-caps"] = JsonSerializer.SerializeToElement(true) }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var state = (await Client.GetFromJsonAsync<LayoutStateDto>(Base))!;
        (state.Template, state.Trim).ShouldBe(("novel-6x9", "5.5x8.5"));
        state.Templates.Single(t => t.Slug == "novel-6x9").Options.Single(o => o.Key == "drop-caps").Value.GetBoolean().ShouldBeTrue();

        var preview = (await (await Client.PostAsJsonAsync(Base + "/preview", Chapter(template: null))).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;
        (preview.Template, preview.Trim).ShouldBe(("novel-6x9", "5.5x8.5"));
        preview.Options["drop-caps"].GetBoolean().ShouldBeTrue();

        var settings = (await Client.GetFromJsonAsync<JsonElement>($"/api/books/{SeedIds.Books.PlayersGuide}/settings/layout")).ToString();
        settings.ShouldContain("5.5x8.5");
    }

    [Test, Order(7)]
    public async Task Select_WhenTheChoiceIsWrong_ShouldChangeNothing()
    {
        (await CodeAsync(Client.PutAsJsonAsync(Base, new LayoutSelectionRequest("nope")), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PutAsJsonAsync(Base, new LayoutSelectionRequest("novel-6x9", "a4")), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PutAsJsonAsync(Base, new LayoutSelectionRequest("novel-6x9", null,
            new Dictionary<string, JsonElement> { ["drop-caps"] = JsonSerializer.SerializeToElement("yes") })), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PutAsJsonAsync(Base, new LayoutSelectionRequest("novel-6x9", null,
            new Dictionary<string, JsonElement> { ["glitter"] = JsonSerializer.SerializeToElement(true) })), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PutAsJsonAsync($"/api/books/{Guid.NewGuid()}/layout", new LayoutSelectionRequest("novel-6x9")), HttpStatusCode.NotFound)).ShouldBe("not_found");

        var state = (await Client.GetFromJsonAsync<LayoutStateDto>(Base))!;
        state.Trim.ShouldBe("5.5x8.5");
    }
}
