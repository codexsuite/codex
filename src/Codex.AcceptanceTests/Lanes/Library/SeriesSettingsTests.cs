using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// Series settings on Forsaken Gods (Players Guide 1, Gamemaster's Codex 2, Bestiary 3).
[TestFixture]
public class SeriesSettingsTests : SeededAcceptanceTest
{
    private static readonly string SeriesId = SeedIds.Series.ForsakenGods.ToString();
    private static readonly string Players = SeedIds.Books.PlayersGuide.ToString();
    private static readonly string Codex = SeedIds.Books.GamemastersCodex.ToString();

    private SeriesScreen Series => new(Page);

    [Test, Order(1)]
    public async Task Rename_WhenBlank_ShouldShowValidationAndKeepName()
    {
        await Page.GotoAsync(Links.Series(SeriesId));
        await Series.SettingsButton.ClickAsync();
        await Expect(Series.Dialog).ToBeVisibleAsync();

        await Series.NameInput.FillAsync("   ");
        await Series.Save.ClickAsync();

        await Expect(Series.NameError).ToBeVisibleAsync();
        await Expect(Series.Dialog).ToBeVisibleAsync();
        await Series.Cancel.ClickAsync();
        await Expect(Series.Dialog).ToBeHiddenAsync();
        await Expect(Series.Title).ToHaveTextAsync("Forsaken Gods");
        await Expect(Series.SettingsButton).ToBeFocusedAsync();
    }

    [Test, Order(2)]
    public async Task Reorder_WhenSaved_ShouldRenumberBooks()
    {
        await Page.GotoAsync(Links.Series(SeriesId));
        await Series.SettingsButton.ClickAsync();
        await Expect(Series.Dialog).ToBeVisibleAsync();

        await Series.Grip(Codex).FocusAsync();
        await Page.Keyboard.PressAsync("Alt+ArrowUp");
        await Expect(Series.OrderNumber(Codex)).ToHaveTextAsync("001");
        await Expect(Series.OrderNumber(Players)).ToHaveTextAsync("002");

        var refetch = await Page.RunAndWaitForApiAsync("/api/library", "GET", () => Series.Save.ClickAsync());

        await Expect(Series.Dialog).ToBeHiddenAsync();
        var library = JsonDocument.Parse(await refetch.TextAsync()).RootElement;
        var numbers = library.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == SeriesId).GetProperty("books").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("seriesNumber").GetInt32());
        numbers[Codex].ShouldBe(1);
        numbers[Players].ShouldBe(2);
        numbers[SeedIds.Books.BestiaryOfTheFallen.ToString()].ShouldBe(3);
        await Expect(Series.Tile(Codex)).ToContainTextAsync("Book 001");
        await Expect(Series.Tile(Players)).ToContainTextAsync("Book 002");
    }

    [Test, Order(3)]
    public async Task Reorder_WhenReloaded_ShouldKeepNewNumbers()
    {
        await Page.GotoAsync(Links.Library());
        // The first load's GET must finish before the reload, or the wait can catch it and lose its body to the navigation.
        await Expect(new LibraryScreen(Page).StackGroup).ToBeVisibleAsync();
        var response = await Page.RunAndWaitForApiAsync("/api/library", "GET", () => Page.ReloadAsync());

        var library = JsonDocument.Parse(await response.TextAsync()).RootElement;
        var first = library.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == SeriesId).GetProperty("books")[0];
        first.GetProperty("id").GetString().ShouldBe(Codex);
        first.GetProperty("seriesNumber").GetInt32().ShouldBe(1);
    }
}
