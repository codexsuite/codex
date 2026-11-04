using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J23 · Find and replace in the manuscript (docs/journeys-wave5.md): rename a person (a mention) and a term across the
// book without damaging cited passages or links. Where the seed differs from the doc (the Ch 3 · Sc 1 "teams" sits inside
// the Beck paraphrase, so it is locked too) the test follows the seed; counts outside the named scenes are computed from
// prose.body, never typed in.
[TestFixture, Category("J23")]
public class J23_FindAndReplace : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Ch3Sc1 = SeedIds.Scenes.Ch3Sc1;
    private static readonly Guid Ch9Sc1 = SeedIds.Scenes.Ch9Sc1;
    private static readonly Guid Ch5Sc2 = SeedIds.Scenes.Ch5Sc2;

    private Task<string> BodyAsync(Guid scene) =>
        ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", scene))!;

    private Task<long> RevisionsAsync(Guid scene) =>
        ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @scene", ("scene", scene));

    // Whole-word "teams" in every live scene's prose, the same regexp the server's rule uses over the bodies.
    private Task<long> TeamsAsync() =>
        ScalarSqlAsync<long>(
            """
            SELECT coalesce(sum((SELECT count(*) FROM regexp_matches(p.body, '\mteams\M', 'gi'))), 0)::bigint
            FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @book AND s.archived_at IS NULL
            """, ("book", SeedIds.Books.ProductEngineering));

    private async Task OpenReplaceAsync(WritePageObject write, string find, string replacement)
    {
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Scene(Ch3Sc1)).ToBeVisibleAsync();
        await write.Find.FillAsync(find);
        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceInput.FillAsync(replacement);
    }

    [Test, Order(1)]
    public async Task Step1_WhenKentBeckIsFound_ShouldCountOneMatchAndOfferReplace()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Scene(Ch3Sc1)).ToBeVisibleAsync();

        await write.Find.FillAsync("Kent Beck");
        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceInput.FillAsync("Kenneth Beck");

        await Expect(write.FindCount).ToContainTextAsync("1");
        await Expect(write.ReplaceCount).ToContainTextAsync("1 match in 1 scene");
        await Expect(write.ReplaceAll).ToBeEnabledAsync();
    }

    [Test, Order(2)]
    public async Task Step2_OnTheOptionsRow_ShouldDefaultToProseOnlyWithNoCaseOrWordMatchingOrArchived()
    {
        var write = new WritePageObject(Page);

        await OpenReplaceAsync(write, "Kent Beck", "Kenneth Beck");

        await Expect(write.ReplaceProse).ToBeCheckedAsync();
        await Expect(write.ReplaceSummaries).Not.ToBeCheckedAsync();
        await Expect(write.ReplaceBeats).Not.ToBeCheckedAsync();
        await Expect(write.ReplaceMatchCase).Not.ToBeCheckedAsync();
        await Expect(write.ReplaceWholeWord).Not.ToBeCheckedAsync();
        await Expect(write.ReplaceArchived).Not.ToBeCheckedAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenReplaceAll_ShouldRenameTheMentionKeepItsLinkAndCutARevision()
    {
        var write = new WritePageObject(Page);
        var before = await BodyAsync(Ch3Sc1);
        var revisions = await RevisionsAsync(Ch3Sc1);
        var words = await ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", Ch3Sc1));
        await OpenReplaceAsync(write, "Kent Beck", "Kenneth Beck");
        await Expect(write.ReplaceAll).ToBeEnabledAsync();

        await write.ReplaceAll.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Replaced 1 in 1 scene");
        var body = await BodyAsync(Ch3Sc1);
        body.ShouldContain($"[Kenneth Beck](kb:{SeedIds.KbEntries.KentBeck})");
        body.ShouldNotBe(before);
        (await ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", Ch3Sc1))).ShouldBe(words);
        words.ShouldBe(2650);
        (await RevisionsAsync(Ch3Sc1)).ShouldBeGreaterThan(revisions);
        (await ScalarSqlAsync<string>("SELECT name FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.KentBeck))).ShouldBe("Kent Beck");
        await Expect(write.Mentions(Ch3Sc1).First).ToContainTextAsync("Kenneth Beck", new() { Timeout = 10_000 });
    }

    [Test, Order(4)]
    public async Task Step4_WhenTheScopeIsChapter9_ShouldFindTwoLockedMatchesAndReplaceNothing()
    {
        var write = new WritePageObject(Page);
        var before = await BodyAsync(Ch9Sc1);
        var revisions = await RevisionsAsync(Ch9Sc1);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Scene(Ch9Sc1)).ToBeVisibleAsync();
        await write.ScopeSelect.SelectOptionAsync($"chapter:{SeedIds.Chapters.Ch9}");
        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceWholeWord.CheckAsync();

        await write.Find.FillAsync("teams");
        await write.ReplaceInput.FillAsync("groups");

        await Expect(write.FindCount).ToContainTextAsync("2");
        await Expect(write.ReplaceItems).ToHaveCountAsync(2);
        await Expect(write.ReplaceItem(0)).ToHaveAttributeAsync("data-status", "cited");
        await Expect(write.ReplaceItem(0)).ToContainTextAsync("Locked");
        await Expect(write.ReplaceAll).ToBeDisabledAsync();
        (await BodyAsync(Ch9Sc1)).ShouldBe(before);
        (await RevisionsAsync(Ch9Sc1)).ShouldBe(revisions);
    }

    [Test, Order(5)]
    public async Task Step5_WhenReplacingOneMatchAndMovingOn_ShouldChangeOnlyThatOne()
    {
        var write = new WritePageObject(Page);
        var total = await TeamsAsync();
        await OpenReplaceAsync(write, "teams", "groups");
        await write.ReplaceWholeWord.CheckAsync();
        await Expect(write.ReplaceItems.First).ToBeVisibleAsync();
        await Expect(write.ReplaceCount).ToContainTextAsync($"{total} matches");
        var index = 0;
        while (await write.ReplaceItem(index).GetAttributeAsync("data-status") != "replaceable")
        {
            index++;
        }

        await write.ReplaceItem(index).ClickAsync();

        await write.ReplaceOne.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Replaced 1 in 1 scene");
        (await TeamsAsync()).ShouldBe(total - 1);
        await write.ReplaceNext.ClickAsync();
        await Expect(write.ReplaceCount).ToContainTextAsync($"{total - 1} matches");
    }

    [Test, Order(6)]
    public async Task Step6_WhenSummariesAreOn_ShouldChangeTheSummaryAndLeaveTheCitedMarker()
    {
        var write = new WritePageObject(Page);
        await ScalarSqlAsync<int>("UPDATE scene SET summary = 'We design the teams first.' WHERE id = @scene RETURNING 1", ("scene", Ch9Sc1));
        var before = await BodyAsync(Ch9Sc1);
        await OpenReplaceAsync(write, "design the teams", "shape the groups");
        await write.ReplaceSummaries.CheckAsync();
        await Expect(write.ReplaceCount).ToContainTextAsync("2 matches");

        await write.ReplaceAll.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Replaced 1 in 1 scene");
        (await ScalarSqlAsync<string>("SELECT summary FROM scene WHERE id = @scene", ("scene", Ch9Sc1))).ShouldBe("We shape the groups first.");
        var body = await BodyAsync(Ch9Sc1);
        Regex.Matches(body, @"\(ref:").Count.ShouldBe(Regex.Matches(before, @"\(ref:").Count);
        body.ShouldContain("design the teams for the architecture you want");
    }

    [Test, Order(7)]
    public async Task Step7_WhenAPendingDraftHoldsMatches_ShouldNoteThemAndLeaveTheDraftOutput()
    {
        var write = new WritePageObject(Page);
        var output = await ScalarSqlAsync<string>("SELECT output FROM ai_generation WHERE id = @id", ("id", SeedIds.AiGenerations.Ch5Sc2Pending));
        var word = Regex.Match(output!, @"\b[A-Za-z]{6,}\b").Value;
        await Page.GotoAsync(Links.Write(BookId));
        await write.ScopeSelect.SelectOptionAsync($"scene:{Ch5Sc2}");
        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceWholeWord.CheckAsync();

        await write.Find.FillAsync(word);
        await write.ReplaceInput.FillAsync("Changed");

        await Expect(write.ReplaceDrafts).ToContainTextAsync("in a pending draft");
        await Expect(write.ReplaceAll).ToBeDisabledAsync();
        (await ScalarSqlAsync<string>("SELECT output FROM ai_generation WHERE id = @id", ("id", SeedIds.AiGenerations.Ch5Sc2Pending))).ShouldBe(output);
    }

    [Test, Order(8)]
    public async Task Step8_WhenUndoIsClicked_ShouldRestoreEveryTouchedSceneAsOneAction()
    {
        var write = new WritePageObject(Page);
        const string Snapshot = "SELECT md5(string_agg(scene_id::text || body, '|' ORDER BY scene_id)) FROM prose";
        var before = await ScalarSqlAsync<string>(Snapshot);
        await OpenReplaceAsync(write, "teams", "groups");
        await write.ReplaceWholeWord.CheckAsync();
        await Expect(write.ReplaceAll).ToBeEnabledAsync();
        await write.ReplaceAll.ClickAsync();
        await Expect(write.Toast).ToContainTextAsync("Replaced");
        (await ScalarSqlAsync<string>(Snapshot)).ShouldNotBe(before);

        var undone = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/replace/undo", "POST", () => write.ToastUndo.ClickAsync());

        undone.Status.ShouldBe(200);
        await Expect(write.ReplaceCount).ToContainTextAsync("matches");
        (await ScalarSqlAsync<string>(Snapshot)).ShouldBe(before);
    }
}
