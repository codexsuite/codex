using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J23 negative paths: nothing to replace, nothing written. Each test reads the prose before and after.
[TestFixture, Category("J23")]
public class J23_FindAndReplace_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Ch3Sc1 = SeedIds.Scenes.Ch3Sc1;
    private static readonly Guid Ch7Sc1 = SeedIds.Scenes.Ch7Sc1;

    private const string Snapshot = "SELECT md5(string_agg(scene_id::text || body, '|' ORDER BY scene_id)) FROM prose";

    private async Task<WritePageObject> OpenAsync()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Scene(Ch3Sc1)).ToBeVisibleAsync();
        return write;
    }

    [Test, Order(1)]
    public async Task EmptyFind_ShouldDisableReplaceAll()
    {
        var write = await OpenAsync();

        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceInput.FillAsync("anything");

        await Expect(write.ReplaceAll).ToBeDisabledAsync();
        await Expect(write.ReplaceOne).ToBeDisabledAsync();
    }

    [Test, Order(2)]
    public async Task NoMatches_ShouldSayNoMatchesCountZeroAndOfferNothingToReplace()
    {
        var write = await OpenAsync();
        await write.ReplaceToggle.ClickAsync();
        await write.ReplaceInput.FillAsync("whatever");

        await write.Find.FillAsync("zzyzx-not-in-the-book");

        await Expect(write.FindCount).ToHaveTextAsync("0");
        await Expect(write.ReplaceCount).ToHaveTextAsync("No matches");
        await Expect(write.ReplaceAll).ToBeDisabledAsync();
    }

    [Test, Order(3)]
    public async Task ReplacementEqualToTheSearch_ShouldDisableReplace()
    {
        var write = await OpenAsync();
        await write.ReplaceToggle.ClickAsync();
        await write.Find.FillAsync("Kent Beck");

        await write.ReplaceInput.FillAsync("Kent Beck");

        await Expect(write.ReplaceCount).ToContainTextAsync("1 match");
        await Expect(write.ReplaceAll).ToBeDisabledAsync();
        await Expect(write.ReplaceOne).ToBeDisabledAsync();
    }

    [Test, Order(4)]
    public async Task Esc_WhenPressedInTheFindBox_ShouldClearItAndChangeNoText()
    {
        var write = await OpenAsync();
        var before = await ScalarSqlAsync<string>(Snapshot);
        await write.Find.FillAsync("Kent Beck");

        await write.Find.PressAsync("Escape");

        await Expect(write.Find).ToHaveValueAsync("");
        (await ScalarSqlAsync<string>(Snapshot)).ShouldBe(before);
    }

    [Test, Order(5)]
    public async Task Replace_WhenAMatchSpansALink_ShouldShowItSkipItAndSayWhy()
    {
        await ScalarSqlAsync<int>(
            "UPDATE prose SET body = body || E'\\n\\nGreat people like Kent [Beck](kb:' || @entry || E') shaped it.\\n', version = version + 1 WHERE scene_id = @scene RETURNING 1",
            ("entry", SeedIds.KbEntries.KentBeck.ToString()), ("scene", Ch7Sc1));
        var before = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Ch7Sc1));
        var write = await OpenAsync();
        await write.ScopeSelect.SelectOptionAsync($"scene:{Ch7Sc1}");
        await write.ReplaceToggle.ClickAsync();
        await write.Find.FillAsync("Kent Beck");
        await write.ReplaceInput.FillAsync("Kenneth Beck");

        await Expect(write.ReplaceItems).ToHaveCountAsync(1);
        await Expect(write.ReplaceItem(0)).ToHaveAttributeAsync("data-status", "spans_link");
        await Expect(write.ReplaceItem(0)).ToContainTextAsync("spans a link");
        await Expect(write.ReplaceAll).ToBeDisabledAsync();
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Ch7Sc1))).ShouldBe(before);
    }
}
