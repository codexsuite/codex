using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Journeys;

// J7 · Add a Knowledge base entry and use it (#16 Part 2): Team Topologies as a Research entry, linked to Ch 9 · Sc 1
// on Plan, then in Write an alias underline, a mention chip from a drag, a Codex addition on Conway's Law (and that
// note on Conway's Timeline, where it can be edited and a second one added), beat context, and the source's Codex link.
[TestFixture, Category("J7")]
public class J07_AddAKnowledgeBaseEntry : JourneyTest
{
    private const string AdditionText = "From here the reader knows the inverse Conway manoeuvre.";
    private const string EditedAdditionText = "From here the reader knows the inverse Conway manoeuvre, and why it works.";
    private const string ClosingNote = "The book closes on the same advice.";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch9Sc1;

    private static Guid _entry;
    private static Guid _addition;

    private KbEntryWindow Window => new(Page);

    private static string EntryUrl(Guid entryId) => Links.Plan(BookId, Links.Hash(("kb", entryId.ToString()), ("entry", entryId.ToString())));

    [Test, Order(1)]
    public async Task Step1_WhenNewEntryCreatedInResearch_ShouldSaveNameAliasesAndSummary()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(new KbSidebar(Page).Category(SeedIds.KbCategories.Research)).ToContainTextAsync("Research");

        await Window.NewEntryButton(SeedIds.KbCategories.Research).ClickAsync();
        await Expect(Window.DraftName).ToBeFocusedAsync();
        var created = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb/entries", "POST", async () =>
        {
            await Window.DraftName.FillAsync("Team Topologies");
            await Window.DraftName.PressAsync("Enter");
        });
        created.Status.ShouldBe((int)HttpStatusCode.Created);
        await Expect(Window.Name).ToHaveValueAsync("Team Topologies");
        _entry = Guid.Parse(Regex.Match(Page.Url, "entry=([0-9a-f-]{36})").Groups[1].Value);

        foreach (var alias in new[] { "team types", "interaction modes" })
        {
            await Window.AddAlias.ClickAsync();
            var patched = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{_entry}", "PATCH", async () =>
            {
                await Window.AliasInput.FillAsync(alias);
                await Window.AliasInput.PressAsync("Enter");
            });
            patched.Status.ShouldBe((int)HttpStatusCode.OK);
            await Expect(Window.Alias(alias)).ToBeVisibleAsync();
        }

        // The first blur after the aliases also saves the (still empty) text parts, so wait for the save that carries the summary.
        var saved = await Page.RunAndWaitForResponseAsync(async () =>
        {
            await Window.Summary.FillAsync("Four team types and three interaction modes for organising teams around the flow of change.");
            await Window.Name.FocusAsync();
        }, r => r.Request.Method == "PATCH" && new Uri(r.Url).AbsolutePath == $"/api/kb/entries/{_entry}"
            && r.Request.PostData!.Contains("Four team types", StringComparison.Ordinal));
        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Page.ReloadAsync();
        await Expect(Window.Summary).ToHaveValueAsync(new Regex("^Four team types"));
        (await ScalarSqlAsync<Guid>("SELECT category_id FROM kb_entry WHERE id = @id", ("id", _entry))).ShouldBe(SeedIds.KbCategories.Research);
        (await ScalarSqlAsync<string[]>("SELECT aliases FROM kb_entry WHERE id = @id", ("id", _entry))).ShouldBe(["team types", "interaction modes"]);
        (await ScalarSqlAsync<string>("SELECT summary FROM kb_entry WHERE id = @id", ("id", _entry))).ShouldStartWith("Four team types");
    }

    [Test, Order(2)]
    public async Task Step2_WhenEntryDraggedOntoCh9Sc1Card_ShouldShowLinkedChipAndSaveSceneKb()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.ActToggle(SeedIds.Acts.Practices).ClickAsync();
        await Expect(plan.Scene(Scene)).ToBeVisibleAsync();
        var item = new KbSidebar(Page).Item(_entry);
        await Expect(item).ToBeVisibleAsync();
        await plan.Scene(Scene).ScrollIntoViewIfNeededAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/kb", "POST", () => item.DragToAsync(plan.Scene(Scene)));

        response.Ok.ShouldBeTrue();
        await Expect(plan.Entry(Scene, _entry)).ToContainTextAsync("Team Topologies");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NULL", ("scene", Scene), ("entry", _entry))).ShouldBe(1);
    }

    [Test, Order(3)]
    public async Task Step3_WhenTeamTypesTypedInCh9Sc1_ShouldGetDottedUnderlineInResearchColour()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "9.1"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(write.Aliases(Scene)).ToHaveCountAsync(0);

        // Deviation: the seeded Ch 9 · Sc 1 prose has no "team types" in it, so the journey types the words first.
        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose", "PUT", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("Two team types matter most here. ");
            await write.Manuscript.FocusAsync();
        });

        saved.Ok.ShouldBeTrue();
        var alias = write.Aliases(Scene);
        await Expect(alias).ToHaveCountAsync(1);
        await Expect(alias).ToHaveTextAsync("team types");
        await Expect(alias).ToHaveAttributeAsync("data-kb-entry", _entry.ToString());
        await Expect(alias).ToHaveCSSAsync("border-bottom-style", "dotted");
        var colour = await ScalarSqlAsync<string>("SELECT color FROM kb_category WHERE id = @id", ("id", SeedIds.KbCategories.Research));
        await Expect(alias).ToHaveAttributeAsync("style", new Regex($"--cat: var\\(--{Regex.Escape(colour!)}"));
    }

    [Test, Order(4)]
    public async Task Step4_WhenEntryDraggedIntoText_ShouldBecomeMentionChip()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "9.1"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(write.Aliases(Scene)).ToHaveCountAsync(1);
        var item = new KbSidebar(Page).Item(_entry);
        await Expect(item).ToBeVisibleAsync();
        var mentionsBefore = await write.Mentions(Scene).CountAsync();

        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose", "PUT", async () =>
        {
            // Drop on the underlined words: a line of text, clear of the cards and the gutter.
            await item.DragToAsync(write.Aliases(Scene).First);
            await write.Manuscript.FocusAsync();
        });

        saved.Ok.ShouldBeTrue();
        await Expect(write.Mentions(Scene)).ToHaveCountAsync(mentionsBefore + 1);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 10_000 });
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene))).ShouldNotBeNull().ShouldContain($"(kb:{_entry})");
    }

    [Test, Order(5)]
    public async Task Step5_WhenCodexAdditionAddedForConwaysLaw_ShouldSaveKbProgressionAndLine()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "9.1"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        var cards = await write.AdditionCards(Scene).CountAsync();

        var inserted = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/kb-additions", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("/codex");
            await write.SlashItem(WriteTestIds.SlashCodexAddition).ClickAsync();
            await write.EntryPickerOption(SeedIds.KbEntries.ConwaysLaw).ClickAsync();
        });

        inserted.Status.ShouldBe((int)HttpStatusCode.OK);
        _addition = JsonSerializer.Deserialize<InsertKbAdditionResult>(await inserted.TextAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ProgressionId;
        await Expect(write.AdditionCards(Scene)).ToHaveCountAsync(cards + 1);
        await Expect(write.AdditionEntry(_addition)).ToContainTextAsync("Conway's Law");
        var patched = await Page.RunAndWaitForApiAsync($"/api/kb-additions/{_addition}", "PATCH", async () =>
        {
            await write.AdditionBody(_addition).FillAsync(AdditionText);
            await write.Manuscript.FocusAsync();
        });
        patched.Ok.ShouldBeTrue();
        (await ScalarSqlAsync<string>("SELECT body FROM kb_progression WHERE id = @id", ("id", _addition))).ShouldBe(AdditionText);
        (await ScalarSqlAsync<Guid>("SELECT kb_entry_id FROM kb_progression WHERE id = @id", ("id", _addition))).ShouldBe(SeedIds.KbEntries.ConwaysLaw);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene)))
            .ShouldNotBeNull().ShouldContain($"::kb-addition{{id=\"{_addition}\"}}");
    }

    [Test, Order(6)]
    public async Task Step6_OnConwaysTimeline_ShouldListTheAdditionEditItAndAddAnotherNote()
    {
        var conway = SeedIds.KbEntries.ConwaysLaw;
        await Page.GotoAsync(EntryUrl(conway));
        await Expect(Window.Title).ToHaveTextAsync("Conway's Law");
        await Window.Tab("timeline").ClickAsync();
        await Expect(Window.TimelineNotes).ToHaveCountAsync(1);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync("From scene 9.1");
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync(AdditionText);

        await Window.TimelineEdit(_addition).ClickAsync();
        await Window.TimelineEditText(_addition).FillAsync(EditedAdditionText);
        var edited = await Page.RunAndWaitForApiAsync($"/api/kb-additions/{_addition}", "PATCH", () => Window.TimelineEditSave(_addition).ClickAsync());
        edited.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync(EditedAdditionText);

        await Window.TimelineAdd.ClickAsync();
        await Window.TimelineAddScene.SelectOptionAsync(SeedIds.Scenes.Ch10Sc1.ToString());
        await Window.TimelineAddText.FillAsync(ClosingNote);
        var added = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch10Sc1}/kb-additions", "POST", () => Window.TimelineAddSubmit.ClickAsync());

        added.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(Window.TimelineNotes).ToHaveCountAsync(2);
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync("From scene 10.1");
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync(ClosingNote);
        (await ScalarSqlAsync<string>("SELECT body FROM kb_progression WHERE id = @id", ("id", _addition))).ShouldBe(EditedAdditionText);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prose WHERE scene_id = @scene AND body LIKE '%::kb-addition{id=%'", ("scene", SeedIds.Scenes.Ch10Sc1))).ShouldBe(1);
    }

    [Test, Order(7)]
    public async Task Step7_WhenTeamTopologiesAddedAsBeatContext_ShouldShowChipAndSaveBeatKb()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "9.1"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();

        // Deviation: the seed has no beat in Ch 9, so the journey drops one in first (as J6 does).
        var inserted = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/beats", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("/scene");
            await write.SlashItem(WriteTestIds.SlashSceneBeat).ClickAsync();
        });
        var beat = JsonSerializer.Deserialize<InsertBeatResult>(await inserted.TextAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.BeatId;
        await Expect(write.BeatContextAdd(beat)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/beats/{beat}/context", "PUT", async () =>
        {
            await write.BeatContextAdd(beat).ClickAsync();
            await write.BeatContextSearch.FillAsync("Team Top");
            await write.BeatContextOption(_entry).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.NoContent);
        await Expect(write.BeatContextChip(beat, _entry)).ToContainTextAsync("Team Topologies");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM beat_kb WHERE beat_id = @beat AND kb_entry_id = @entry AND archived_at IS NULL", ("beat", beat), ("entry", _entry))).ShouldBe(1);
    }

    [Test, Order(8)]
    public async Task Variant_WhenSourceNineLinkedToTheEntry_ShouldShowItOnTheEntryAndInReferences()
    {
        var references = new ReferencesScreen(Page);
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.References(BookId));
        await references.Row(SeedIds.References.TeamTopologies).ClickAsync();
        await Expect(panel.Title).ToContainTextAsync("Team Topologies");

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.TeamTopologies}", "PATCH", () => panel.Entry.SelectOptionAsync(_entry.ToString()));

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<Guid>("SELECT kb_entry_id FROM reference WHERE id = @id", ("id", SeedIds.References.TeamTopologies))).ShouldBe(_entry);
        await Page.ReloadAsync();
        await references.Row(SeedIds.References.TeamTopologies).ClickAsync();
        await Expect(panel.Entry).ToHaveValueAsync(_entry.ToString());
        await Page.GotoAsync(EntryUrl(_entry));
        await Expect(Page.GetByTestId(KbTestIds.Source)).ToContainTextAsync("Team Topologies");
    }
}
