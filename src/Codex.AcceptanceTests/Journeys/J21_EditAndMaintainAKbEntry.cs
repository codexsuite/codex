using System.Net;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J21 · Edit and maintain a Knowledge base entry (#16 wave 5, L17.1): rename Goodhart's Law with its old name kept as an
// alias, alias collisions and warnings, change its category, switch on always in context, relate it to Conway's Law, merge a
// new duplicate into DORA Four Keys, delete it and Undo (R2), edit categories and labels, then read its Timeline.
[TestFixture, Category("J21")]
public class J21_EditAndMaintainAKbEntry : JourneyTest
{
    private const string Renamed = "Goodhart's Law (metrics)";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Goodhart = SeedIds.KbEntries.GoodhartsLaw;

    private static readonly FakeProseGenerator Generator = new(["A drafted line."]);

    private static Guid _dora;

    // Step 4 drafts a beat: Generate needs a connected AI provider (#36), and the fake generator records what it is sent.
    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    [OneTimeSetUp]
    public async Task ConnectAnAiProviderAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private KbEntryWindow Window => new(Page);

    private KbSidebar Sidebar => new(Page);

    private static string EntryUrl(Guid entryId) => Links.Plan(BookId, Links.Hash(("kb", entryId.ToString()), ("entry", entryId.ToString())));

    private ILocator SidebarItems(Guid categoryId) => Sidebar.Category(categoryId).GetByTestId(new Regex("^shell-kb-item-"));

    private Task<IResponse> SaveAsync(Guid entryId, string marker, Func<Task> action) =>
        Page.RunAndWaitForResponseAsync(action, r => r.Request.Method == "PATCH" && new Uri(r.Url).AbsolutePath == $"/api/kb/entries/{entryId}"
            && r.Request.PostData!.Contains(marker, StringComparison.Ordinal));

    [Test, Order(1)]
    public async Task Step1_WhenGoodhartRenamed_ShouldKeepTheOldNameAsAnAliasAndTheSlug()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(Window.Name).ToHaveValueAsync("Goodhart's Law");

        await Window.Name.FillAsync(Renamed);
        await Expect(Window.RenameKeepAlias).ToBeCheckedAsync();
        var saved = await SaveAsync(Goodhart, "keepOldName", () => Window.Name.PressAsync("Enter"));

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<string>("SELECT slug FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe("goodharts-law");
        (await ScalarSqlAsync<string>("SELECT name FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe(Renamed);
        (await ScalarSqlAsync<bool>("SELECT 'Goodhart''s Law' = ANY (aliases) FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBeTrue();
        await Expect(Window.Alias("Goodhart's Law")).ToBeVisibleAsync();
        await Expect(Window.Root.GetByTestId(KbTestIds.ReferenceId)).ToHaveTextAsync("e:goodharts-law");
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Entry(SeedIds.Scenes.Ch4Sc1, Goodhart)).ToContainTextAsync(Renamed);
    }

    [Test, Order(2)]
    public async Task Step2_WhenAliasesAreAddedThatCollideOrAreCommon_ShouldBlockOrWarn()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(Window.Name).ToHaveValueAsync(Renamed);

        // Another entry's alias: held until Add anyway.
        await Window.AddAlias.ClickAsync();
        await Window.AliasInput.FillAsync("Conway");
        await Window.AliasInput.PressAsync("Enter");
        await Expect(Window.AliasError).ToContainTextAsync("already a name or alias of Conway's Law");
        await Expect(Window.AliasAddAnyway).ToBeVisibleAsync();
        (await ScalarSqlAsync<bool>("SELECT 'Conway' = ANY (aliases) FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBeFalse();
        await Window.AliasInput.PressAsync("Escape");
        await Expect(Window.AliasError).ToHaveCountAsync(0);

        // A short alias is saved, with a warning.
        await Window.AddAlias.ClickAsync();
        var saved = await SaveAsync(Goodhart, "\"law\"", async () =>
        {
            await Window.AliasInput.FillAsync("law");
            await Window.AliasInput.PressAsync("Enter");
        });
        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(Window.AliasWarning).ToContainTextAsync("Underlines every");
        await Window.Alias("law").HoverAsync();
        await SaveAsync(Goodhart, "aliases", () => Page.GetByTestId(KbTestIds.AliasRemove("law")).ClickAsync());
        await Expect(Window.Alias("law")).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Step3_WhenCategoryChangedToResearch_ShouldMoveTheEntryAndTheSidebarCounts()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(SidebarItems(SeedIds.KbCategories.Laws)).ToHaveCountAsync(3);
        var researchBefore = await SidebarItems(SeedIds.KbCategories.Research).CountAsync();

        await Window.CategoryChip.ClickAsync();
        var saved = await SaveAsync(Goodhart, "categoryId", () => Window.CategoryOption(SeedIds.KbCategories.Research).ClickAsync());

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(SidebarItems(SeedIds.KbCategories.Laws)).ToHaveCountAsync(2);
        await Expect(SidebarItems(SeedIds.KbCategories.Research)).ToHaveCountAsync(researchBefore + 1);
        (await ScalarSqlAsync<Guid>("SELECT category_id FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe(SeedIds.KbCategories.Research);
    }

    [Test, Order(4)]
    public async Task Step4_WhenKentBeckAlwaysInContext_ShouldSaveTheFlagAndGenerateShouldSendHisEntry()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Expect(Window.Name).ToHaveValueAsync("Kent Beck");

        var saved = await SaveAsync(SeedIds.KbEntries.KentBeck, "alwaysInContext", () => Window.AlwaysInContext.ClickAsync());

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<bool>("SELECT always_in_context FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.KentBeck))).ShouldBeTrue();

        // A beat whose own context does not name him: the entry goes with the draft because it is always in context.
        var beat = SeedIds.Beats.Ch3Sc3BadScrum;
        (await ScalarSqlAsync<long>("SELECT count(*) FROM beat_kb WHERE beat_id = @beat AND kb_entry_id = @kent AND archived_at IS NULL", ("beat", beat), ("kent", SeedIds.KbEntries.KentBeck))).ShouldBe(0);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        var generated = await Page.RunAndWaitForApiAsync($"/api/beats/{beat}/generate", "POST", () => write.BeatGenerate(beat).ClickAsync());

        generated.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Draft(beat)).ToHaveAttributeAsync("data-state", "pending");
        Generator.Requests[^1].Prompt.ShouldContain("- Kent Beck");
        (await ScalarSqlAsync<bool>(
            "SELECT request->'context' ? 'Kent Beck' FROM ai_generation WHERE beat_id = @beat ORDER BY created_at DESC LIMIT 1", ("beat", beat))).ShouldBeTrue();
    }

    [Test, Order(5)]
    public async Task Step5_WhenConwaysLawRelated_ShouldShowTheChipOnBothEntriesAndStoreOneRow()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(Window.Name).ToHaveValueAsync(Renamed);
        await Window.RelatedAdd.ClickAsync();
        var posted = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{Goodhart}/related", "POST", () => Window.RelatedOption(SeedIds.KbEntries.ConwaysLaw).ClickAsync());

        posted.Ok.ShouldBeTrue();
        await Expect(Window.RelatedChip(SeedIds.KbEntries.ConwaysLaw)).ToBeVisibleAsync();
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.ConwaysLaw));
        await Expect(Window.RelatedChip(Goodhart)).ToBeVisibleAsync();
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM kb_entry_link WHERE entry_a_id = least(@a, @b) AND entry_b_id = greatest(@a, @b) AND archived_at IS NULL",
            ("a", Goodhart), ("b", SeedIds.KbEntries.ConwaysLaw))).ShouldBe(1);
    }

    [Test, Order(6)]
    public async Task Step6_WhenDoraMetricsMergedIntoDoraFourKeys_ShouldRepointItsSceneLinkAndAliasTheName()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        var created = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb/entries", "POST", async () =>
        {
            await Window.NewEntryButton(SeedIds.KbCategories.Research).ClickAsync();
            await Window.DraftName.FillAsync("DORA metrics");
            await Window.DraftName.PressAsync("Enter");
        });
        created.Status.ShouldBe((int)HttpStatusCode.Created);
        await Expect(Window.Name).ToHaveValueAsync("DORA metrics");
        _dora = Guid.Parse(Regex.Match(Page.Url, "entry=([0-9a-f-]{36})").Groups[1].Value);
        await SaveAsync(_dora, "The four delivery metrics.", async () =>
        {
            await Window.Summary.FillAsync("The four delivery metrics.");
            await Window.Name.FocusAsync();
        });

        await Window.Close.ClickAsync();
        await Page.GotoAsync(Links.Plan(BookId));
        var scene = SeedIds.Scenes.Ch5Sc1;
        await plan.Scene(scene).ScrollIntoViewIfNeededAsync();
        var linked = await Page.RunAndWaitForApiAsync($"/api/scenes/{scene}/kb", "POST", () => Sidebar.Item(_dora).DragToAsync(plan.Scene(scene)));
        linked.Ok.ShouldBeTrue();

        await Page.GotoAsync(EntryUrl(_dora));
        await Window.Menu.ClickAsync();
        await Window.MenuMerge.ClickAsync();
        await Expect(Window.MergeOption(_dora)).ToHaveCountAsync(0);
        await Window.MergeOption(SeedIds.KbEntries.DoraFourKeys).ClickAsync();
        await Expect(Window.MergeMessage).ToContainTextAsync("1 scene link, 0 additions, 0 related.");
        await Expect(Window.MergeMessage).ToContainTextAsync("\"DORA metrics\" becomes an alias.");
        var merged = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{_dora}/merge", "POST", () => Window.MergeConfirm.ClickAsync());

        merged.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @id", ("id", _dora))).ShouldBeTrue();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene_kb WHERE kb_entry_id = @id AND archived_at IS NULL", ("id", _dora))).ShouldBe(0);
        (await ScalarSqlAsync<bool>("SELECT 'DORA metrics' = ANY (aliases) FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.DoraFourKeys))).ShouldBeTrue();
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @id AND archived_at IS NULL", ("scene", scene), ("id", SeedIds.KbEntries.DoraFourKeys))).ShouldBe(1);
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.DoraFourKeys));
        await Window.Tab("used-in").ClickAsync();
        // Deviation: DORA Four Keys has no scene links in the seed, so the merge gives it one scene (journeys-wave5 said 3).
        await Expect(Window.UsedInTally).ToContainTextAsync("1 scene");
        await Expect(Sidebar.Item(_dora)).ToHaveCountAsync(0);
    }

    [Test, Order(7)]
    public async Task Step7_WhenGoodhartDeleted_ShouldTurnMentionsToTextAndUndoRestoreThem()
    {
        var wordsBefore = await ScalarSqlAsync<long>("SELECT count(*) FROM kb_progression WHERE kb_entry_id = @id AND archived_at IS NULL", ("id", Goodhart));
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(SidebarItems(SeedIds.KbCategories.Research)).ToContainTextAsync([Renamed], new() { Timeout = 10_000 });
        var researchBefore = await SidebarItems(SeedIds.KbCategories.Research).CountAsync();

        await Window.Menu.ClickAsync();
        await Window.MenuDelete.ClickAsync();
        await Expect(Window.DeleteMessage).ToContainTextAsync("Mentions turn into plain text");
        await Window.DeleteCancel.ClickAsync();
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBeTrue();

        await Window.Menu.ClickAsync();
        await Window.MenuDelete.ClickAsync();
        var deleted = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{Goodhart}", "DELETE", () => Window.DeleteConfirm.ClickAsync());

        deleted.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(SidebarItems(SeedIds.KbCategories.Research)).ToHaveCountAsync(researchBefore - 1);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBeTrue();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_progression WHERE kb_entry_id = @id AND archived_at IS NULL", ("id", Goodhart))).ShouldBe(0);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene_kb WHERE kb_entry_id = @id AND archived_at IS NULL", ("id", Goodhart))).ShouldBe(0);

        var restored = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{Goodhart}/restore", "POST", () => Window.ToastUndo.ClickAsync());

        restored.Ok.ShouldBeTrue();
        await Expect(SidebarItems(SeedIds.KbCategories.Research)).ToHaveCountAsync(researchBefore);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_progression WHERE kb_entry_id = @id AND archived_at IS NULL", ("id", Goodhart))).ShouldBe(wordsBefore);
    }

    [Test, Order(8)]
    public async Task Step8_WhenCategoriesEdited_ShouldRenameRecolourDeleteEmptyAndBlockOneWithEntries()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        var blog = SeedIds.KbCategories.BlogPosts;
        await Sidebar.Category(blog).HoverAsync();
        await Sidebar.CategoryMenu(blog).ClickAsync();
        await Sidebar.CategoryRenameInput.FillAsync("Blog");
        var renamed = await Page.RunAndWaitForApiAsync($"/api/kb/categories/{blog}", "PATCH", () => Sidebar.CategoryRenameSave.ClickAsync());
        renamed.Status.ShouldBe((int)HttpStatusCode.OK);
        var recoloured = await Page.RunAndWaitForApiAsync($"/api/kb/categories/{blog}", "PATCH", () => Sidebar.CategoryColour("category-person").ClickAsync());
        recoloured.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<string>("SELECT name FROM kb_category WHERE id = @id", ("id", blog))).ShouldBe("Blog");
        (await ScalarSqlAsync<string>("SELECT color FROM kb_category WHERE id = @id", ("id", blog))).ShouldBe("category-person");
        await Page.Keyboard.PressAsync("Escape");

        // A new empty category (made from an entry's category menu), then deleted from the heading menu.
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Window.CategoryChip.ClickAsync();
        await Window.NewCategoryOpen.ClickAsync();
        await Window.NewCategoryName.FillAsync("Temp");
        var created = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb/categories", "POST", () => Window.NewCategoryCreate.ClickAsync());
        created.Status.ShouldBe((int)HttpStatusCode.Created);
        await Expect(Window.CategoryChip).ToContainTextAsync("Temp");
        await Window.CategoryChip.ClickAsync();
        await Window.CategoryOption(SeedIds.KbCategories.People).ClickAsync();
        var temp = await ScalarSqlAsync<Guid>("SELECT id FROM kb_category WHERE name = 'Temp' AND book_id = @book", ("book", SeedIds.Books.ProductEngineering));
        await Expect(Sidebar.Category(temp)).ToBeVisibleAsync();
        await Sidebar.Category(temp).HoverAsync();
        await Sidebar.CategoryMenu(temp).ClickAsync();
        var deleted = await Page.RunAndWaitForApiAsync($"/api/kb/categories/{temp}", "DELETE", () => Sidebar.CategoryDelete(temp).ClickAsync());
        deleted.Status.ShouldBe((int)HttpStatusCode.NoContent);
        // A category delete is a soft delete (#78).
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_category WHERE id = @id", ("id", temp))).ShouldBeTrue();

        // Laws still has entries.
        var laws = SeedIds.KbCategories.Laws;
        await Sidebar.Category(laws).HoverAsync();
        await Sidebar.CategoryMenu(laws).ClickAsync();
        await Sidebar.CategoryDelete(laws).ClickAsync();
        await Expect(Sidebar.CategoryMenuError).ToHaveTextAsync("Move or delete its 2 entries first.");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_category WHERE id = @id", ("id", laws))).ShouldBe(1);
    }

    [Test, Order(9)]
    public async Task Step9_WhenLabelsManaged_ShouldRenameDraftingAndDeleteNeedsResearch()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.LabelFilter.ClickAsync();
        await plan.ManageLabels.ClickAsync();

        var renamed = await Page.RunAndWaitForApiAsync($"/api/labels/{SeedIds.Labels.Drafting}", "PATCH", async () =>
        {
            await plan.LabelRename(SeedIds.Labels.Drafting).FillAsync("In progress");
            await plan.LabelRename(SeedIds.Labels.Drafting).PressAsync("Enter");
        });
        renamed.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<string>("SELECT name FROM label WHERE id = @id", ("id", SeedIds.Labels.Drafting))).ShouldBe("In progress");

        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene_label WHERE label_id = @id AND archived_at IS NULL", ("id", SeedIds.Labels.NeedsResearch))).ShouldBe(3);
        await plan.LabelDelete(SeedIds.Labels.NeedsResearch).ClickAsync();
        await Expect(plan.LabelDeleteMessage).ToHaveTextAsync("Used on 3 scenes. Deleting removes it from them.");
        var deleted = await Page.RunAndWaitForApiAsync($"/api/labels/{SeedIds.Labels.NeedsResearch}", "DELETE", () => plan.LabelDeleteConfirm.ClickAsync());

        deleted.Status.ShouldBe((int)HttpStatusCode.NoContent);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM label WHERE id = @id", ("id", SeedIds.Labels.NeedsResearch))).ShouldBeTrue();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM label WHERE book_id = @book AND archived_at IS NULL", ("book", SeedIds.Books.ProductEngineering))).ShouldBe(3);
        await plan.LabelManagerDone.ClickAsync();
        await Expect(plan.LabelFilterOption(SeedIds.Labels.NeedsResearch)).ToHaveCountAsync(0);
        await Expect(plan.LabelFilterOption(SeedIds.Labels.Drafting)).ToContainTextAsync("In progress");
    }

    [Test, Order(10)]
    public async Task Step10_WhenTheRestoredGoodhartsTimelineIsOpened_ShouldListItsAdditionInBookOrder()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Window.Tab("timeline").ClickAsync();

        await Expect(Window.TimelineNotes).ToHaveCountAsync(1);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync("From scene 4.1");
    }

    [Test, Order(11)]
    public async Task Keyboard_WhenEscapePressed_ShouldCloseTheEntryWindow()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Expect(Window.Root).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Window.Root).ToHaveCountAsync(0);
    }
}
