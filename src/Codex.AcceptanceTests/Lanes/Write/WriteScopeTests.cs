using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Write;

// The scope picker and the Format popover, from the seed: Product Engineering is 15 scenes and 13,703 words,
// chapter 5 is 2 scenes and 3,120 words.
[TestFixture]
public class WriteScopeTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string OtherBookId = SeedIds.Books.HymnsForMachines.ToString();

    [Test, Order(1)]
    public async Task Scope_WhenChapterChosen_ShouldNarrowCounts()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ScopeCounts).ToHaveTextAsync("13,703 words · 55 pages · 58m read");

        await write.ScopeSelect.SelectOptionAsync($"chapter:{SeedIds.Chapters.Ch5}");

        await Expect(write.ScopeCounts).ToHaveTextAsync("3,120 words · 13 pages · 13m read");
        await Expect(write.Scene(SeedIds.Scenes.Ch5Sc1)).ToBeVisibleAsync();
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).ToHaveCountAsync(0);
        await Expect(write.MinimapTicks).ToHaveCountAsync(2);
    }

    [Test, Order(2)]
    public async Task Format_WhenChanged_ShouldPersistPerBook()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.SceneSummary(SeedIds.Scenes.Ch5Sc1)).ToBeVisibleAsync();

        await write.FormatButton.ClickAsync();
        await write.FormatOption("width", "wide").ClickAsync();
        await write.FormatOption("summaries", "hide").ClickAsync();

        await Expect(write.Manuscript).ToHaveAttributeAsync("data-width", "wide");
        await Expect(write.SceneSummary(SeedIds.Scenes.Ch5Sc1)).ToBeHiddenAsync();
        var stored = JsonDocument.Parse((await write.StoredFormatAsync(BookId))!).RootElement;
        stored.GetProperty("width").GetString().ShouldBe("wide");
        stored.GetProperty("hideSummaries").GetBoolean().ShouldBeTrue();

        await Page.ReloadAsync();
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-width", "wide");
        await Expect(write.SceneSummary(SeedIds.Scenes.Ch5Sc1)).ToBeHiddenAsync();

        await Page.GotoAsync(Links.Write(OtherBookId));
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-width", "medium");
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-hide-summaries", "false");
        (await write.StoredFormatAsync(OtherBookId)).ShouldBeNull();
    }
}
