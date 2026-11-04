using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J8 · Find everywhere an entry is used (#16 Part 2): Goodhart's Law clicked in the sidebar on Plan highlights its
// scenes, the highlight carries to Write as a toolbar chip stepped with its arrows, Ctrl+click on a mention opens the
// entry window (#18, `#entry=`), and the chip's x clears it.
[TestFixture, Category("J8")]
public class J08_FindEverywhereAnEntryIsUsed : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Goodhart = SeedIds.KbEntries.GoodhartsLaw;
    private static readonly Guid AliasScene = SeedIds.Scenes.Ch5Sc1;

    [Test, Order(1)]
    public async Task Step1_WhenGoodhartsLawClickedOnPlan_ShouldHighlightCh4Sc1AndCh5Sc2Cards()
    {
        var plan = new PlanScreen(Page);
        var sidebar = new KbSidebar(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(sidebar.Item(Goodhart)).ToBeVisibleAsync();
        await Expect(sidebar.Count(Goodhart)).ToHaveTextAsync("2");

        await sidebar.Item(Goodhart).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"kb={Goodhart}"));
        await Expect(plan.Scene(SeedIds.Scenes.Ch4Sc1)).ToHaveAttributeAsync("data-emphasis", "hit");
        await Expect(plan.Scene(SeedIds.Scenes.Ch5Sc2)).ToHaveAttributeAsync("data-emphasis", "hit");
        await Expect(plan.Scene(SeedIds.Scenes.Ch3Sc1)).ToHaveAttributeAsync("data-emphasis", "dim");
    }

    [Test, Order(2)]
    public async Task Step2_WhenWriteTabOpened_ShouldCarryHighlightAsChipAndAccentTicks()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await new KbSidebar(Page).Item(Goodhart).ClickAsync();
        await new BookBar(Page).TabWrite.ClickAsync();

        await Expect(write.HighlightName).ToHaveTextAsync("Goodhart's Law");
        await Expect(write.HighlightPosition).ToHaveTextAsync(new Regex(@"^1 of \d+$"));
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch4Sc1)).ToHaveAttributeAsync("data-used", "true");
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch3Sc1)).ToHaveAttributeAsync("data-used", "false");
    }

    [Test, Order(3)]
    public async Task Step3_WhenChipArrowsPressed_ShouldScrollToAdditionCardThenToAliasMatch()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(AliasScene)).ToBeVisibleAsync();

        // Deviation: as seeded, Goodhart's Law is used in one place only (the Ch 4 · Sc 1 Codex addition; Ch 5 · Sc 2
        // is a beat and an unapplied draft, with no prose), so there is nothing to step to. The journey says "1 of n",
        // so the author's alias match is typed first, as WriteKbTests does: n becomes 2.
        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{AliasScene}/prose", "PUT", async () =>
        {
            await write.PlaceCaretAtStartAsync(AliasScene);
            await Page.Keyboard.TypeAsync("Goodhart noticed the drift. ");
            await write.Manuscript.FocusAsync();
        });
        saved.Ok.ShouldBeTrue();
        await Expect(write.Aliases(AliasScene)).ToHaveCountAsync(1);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("kb", Goodhart.ToString()))));
        await Expect(write.HighlightName).ToHaveTextAsync("Goodhart's Law");
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");

        await write.HighlightNext.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");
        await Expect(write.AdditionCards(SeedIds.Scenes.Ch4Sc1)).ToBeInViewportAsync();
        await write.HighlightNext.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("2 of 2");
        await Expect(write.Aliases(AliasScene)).ToBeInViewportAsync();
        await write.HighlightPrevious.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");
        await Expect(write.AdditionCards(SeedIds.Scenes.Ch4Sc1)).ToBeInViewportAsync();
    }

    [Test, Order(4)]
    public async Task Step4_WhenMentionCtrlClicked_ShouldOpenEntryWindowWithEntryInHash()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("kb", Goodhart.ToString()))));
        var mention = write.Mentions(SeedIds.Scenes.Ch3Sc1).First;
        await Expect(mention).ToBeVisibleAsync();

        // Deviation: the seed has no mention of Goodhart's Law (the highlight is not on a mention); the seeded Kent
        // Beck mention in Ch 3 · Sc 1 stands in. Per #18 the result is the entry window, not the toast of the design.
        await mention.ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Control] });

        await Expect(Page).ToHaveURLAsync(new Regex($"#.*entry={SeedIds.KbEntries.KentBeck}"));
        var window = new KbSidebar(Page);
        await Expect(window.EntryWindow).ToBeVisibleAsync();
        await Expect(window.EntryWindowTitle).ToHaveTextAsync("Kent Beck");
    }

    [Test, Order(5)]
    public async Task Step5_WhenChipCrossClicked_ShouldClearHighlightAndTicks()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("kb", Goodhart.ToString()))));
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch4Sc1)).ToHaveAttributeAsync("data-used", "true");

        await write.HighlightClear.ClickAsync();

        await Expect(write.HighlightChip).ToHaveCountAsync(0);
        await Expect(Page).Not.ToHaveURLAsync(new Regex("kb="));
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch4Sc1)).ToHaveAttributeAsync("data-used", "false");
        (await Page.EvaluateAsync<string?>("id => sessionStorage.getItem('codex.hl.' + id)", BookId)).ShouldBeNull();
    }
}
