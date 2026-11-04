using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Import;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Journeys;

// J3 · Start a new book (#16 Part 2): A standalone (Library -> New book -> Plan), B in a series (Series -> + Add book),
// and the Import variant. Steps 1-5 are A, 6-8 are B, 9 is the Import variant.
// Deviation: the New book dialog has no author field (title, series, number, target words), so "author (defaults to JD)"
// is not asserted.
[TestFixture, Category("J3")]
public partial class J03_StartANewBook : JourneyTest
{
    private static readonly string SeriesId = SeedIds.Series.AshenCrown.ToString();
    private static readonly string SmallFolder = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "markdown-small");

    private static string _bookId = "";
    private static string _seriesBookId = "";
    private static Guid _chapterId;
    private static Guid _sceneId;

    private NewBookDialog Dialog => new(Page);

    [Test, Order(1)]
    public async Task Step1_WhenNewBookClicked_ShouldOpenTheDialog()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Dialog.OpenButton).ToBeVisibleAsync();

        await Dialog.OpenButton.ClickAsync();

        await Expect(Dialog.Root).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Step2_OnDialog_ShouldTakeTitleSeriesNoneAndTarget()
    {
        await Page.GotoAsync(Links.Library());
        await Dialog.OpenButton.ClickAsync();

        await Dialog.Title.FillAsync("Field Notes");
        await Dialog.Target.FillAsync("60000");

        await Expect(Dialog.Title).ToHaveValueAsync("Field Notes");
        await Expect(Dialog.Series).ToHaveValueAsync("");
        await Expect(Dialog.Target).ToHaveValueAsync("60000");
    }

    [Test, Order(3)]
    public async Task Step3_WhenCreated_ShouldLandOnPlanWithOneEmptyActAndAnEmptySidebar()
    {
        await Page.GotoAsync(Links.Library());
        await Dialog.OpenButton.ClickAsync();
        await Dialog.Title.FillAsync("Field Notes");
        await Dialog.Target.FillAsync("60000");

        var response = await Page.RunAndWaitForApiAsync("/api/books", "POST", () => Dialog.Create.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        _bookId = PlanUrl().Match(Page.Url).Groups[1].Value;
        var plan = new PlanScreen(Page);
        await Expect(plan.Board).ToBeVisibleAsync();
        await Expect(plan.ChapterHeadings).ToHaveCountAsync(0);
        var outline = await OutlineAsync(_bookId);
        outline.GetProperty("acts").GetArrayLength().ShouldBe(1);
        var act = Guid.Parse(outline.GetProperty("acts")[0].GetProperty("id").GetString()!);
        await Expect(plan.ActMeta(act)).ToHaveTextAsync("No chapters yet.");
        await Expect(plan.NewChapter(act)).ToHaveTextAsync("New Chapter");
        await Expect(new KbSidebar(Page).Empty).ToBeVisibleAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE id = @id AND series_id IS NULL AND target_word_count = 60000 AND title = 'Field Notes'",
            ("id", Guid.Parse(_bookId)))).ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task Step4a_WhenNewChapterClicked_ShouldAddChapter1()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(_bookId));
        var act = Guid.Parse((await OutlineAsync(_bookId)).GetProperty("acts")[0].GetProperty("id").GetString()!);

        var response = await Page.RunAndWaitForApiAsync($"/api/acts/{act}/chapters", "POST", () => plan.NewChapter(act).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.TextAsync());
        _chapterId = created.RootElement.GetProperty("id").GetGuid();
        await Expect(plan.Chapter(_chapterId)).ToContainTextAsync("Chapter 1");
    }

    [Test, Order(5)]
    public async Task Step4b_WhenSceneAddedAndSummaryTyped_ShouldAutosave()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(_bookId));
        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{_chapterId}/scenes", "POST", () => plan.NewScene(_chapterId).ClickAsync());
        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.TextAsync());
        _sceneId = created.RootElement.GetProperty("id").GetGuid();
        await Expect(plan.SceneSummaryEditor(_sceneId)).ToBeFocusedAsync();

        // "It autosaves 3 s after the first edit": the save fires on its own, with no click and no sleep.
        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{_sceneId}", "PATCH",
            () => plan.SceneSummaryEditor(_sceneId).PressSequentiallyAsync("Mara finds the first notebook."));

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        (await ScalarSqlAsync<string>("SELECT summary FROM scene WHERE id = @id", ("id", _sceneId)))!.ShouldContain("Mara finds the first notebook.");
    }

    [Test, Order(6)]
    public async Task Step5_WhenAddBookClickedOnSeries_ShouldOpenTheDialog()
    {
        await Page.GotoAsync(Links.Library());
        await new LibraryShelf(Page).SeriesStack(SeriesId).ClickAsync();
        var series = new SeriesScreen(Page);
        await Expect(series.Root).ToBeVisibleAsync();

        await series.AddBook.ClickAsync();

        await Expect(Dialog.Root).ToBeVisibleAsync();
    }

    [Test, Order(7)]
    public async Task Step6_WhenDialogOpenedFromSeries_ShouldPrefillTheAshenCrownAndNumber005()
    {
        await Page.GotoAsync(Links.Series(SeriesId));
        await new SeriesScreen(Page).AddBook.ClickAsync();

        await Expect(Dialog.Series).ToHaveValueAsync(SeriesId);
        // Number 005: the dialog shows the integer the next number is; the book card prints it as "Book 005".
        await Expect(Dialog.Number).ToHaveValueAsync("5");
    }

    [Test, Order(8)]
    public async Task Step7_WhenCreatedInSeries_ShouldLandOnPlanWithTheSeriesSidebar()
    {
        await Page.GotoAsync(Links.Series(SeriesId));
        await new SeriesScreen(Page).AddBook.ClickAsync();
        await Dialog.Title.FillAsync("The Ember Court");

        var response = await Page.RunAndWaitForApiAsync("/api/books", "POST", () => Dialog.Create.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        _seriesBookId = PlanUrl().Match(Page.Url).Groups[1].Value;
        var sidebar = new KbSidebar(Page);
        await Expect(sidebar.Section("series")).ToContainTextAsync("From The Ashen Crown");
        await Expect(sidebar.Section("series").GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownCharacters))).ToContainTextAsync("Characters");
        await Expect(sidebar.Section("series").GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownPlaces))).ToContainTextAsync("Places");
        (await ScalarSqlAsync<int>("SELECT series_number FROM book WHERE id = @id AND series_id = @series",
            ("id", Guid.Parse(_seriesBookId)), ("series", SeedIds.Series.AshenCrown))).ShouldBe(5);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM act WHERE book_id = @id", ("id", Guid.Parse(_seriesBookId)))).ShouldBe(1);
    }

    [Test, Order(9)]
    public async Task Step8_ImportVariant_MarkdownFolderShouldLandOnPlanWithTheSummary()
    {
        var import = new ImportScreen(Page);
        await Page.GotoAsync(Links.Library());

        var preview = await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST", () => import.PickFolderAsync(SmallFolder));
        preview.Status.ShouldBe(200);
        await Expect(import.PreviewTitle).ToHaveTextAsync("The Lantern Road");
        var created = await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => import.Run.ClickAsync());

        created.Status.ShouldBe(201);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        await Expect(import.Banner).ToBeVisibleAsync();
        await Expect(import.BannerText).ToContainTextAsync("2 acts, 3 chapters, 3 scenes and");
        await Expect(import.BannerText).ToContainTextAsync("plus 3 Knowledge base entries.");
    }

    private async Task<JsonElement> OutlineAsync(string bookId)
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/books/{bookId}/outline");
        return (await response.JsonAsync())!.Value;
    }

    [GeneratedRegex(@"/books/([0-9a-f-]{36})/plan$")]
    private static partial Regex PlanUrl();
}
