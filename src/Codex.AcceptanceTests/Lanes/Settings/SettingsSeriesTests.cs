using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Settings;

namespace Codex.AcceptanceTests.Lanes.Settings;

// The Series section on Forsaken Gods (Players Guide 1, Gamemaster's Codex 2, Bestiary 3; the series has the Spells
// category). One story: renumber with Undo, reject bad numbers, then move the Bestiary out and see the copy-down.
[TestFixture]
public class SettingsSeriesTests : SeededAcceptanceTest
{
    private static readonly string Bestiary = SeedIds.Books.BestiaryOfTheFallen.ToString();
    private static readonly string Players = SeedIds.Books.PlayersGuide.ToString();
    private static readonly string Codex = SeedIds.Books.GamemastersCodex.ToString();

    private SettingsScreen Settings => new(Page);

    [Test, Order(1)]
    public async Task Number_WhenChanged_ShouldRenumberAndUndo()
    {
        await Page.GotoAsync(Links.Settings(Bestiary, Links.Hash(("series", null))));
        await Expect(Settings.NumberLine).ToHaveTextAsync("Book 003 · last in the series");

        var change = await Page.RunAndWaitForApiAsync($"/api/books/{Bestiary}/series-number", "PUT",
            () => Settings.NumberSelect.SelectOptionAsync("1"));

        change.Status.ShouldBe(200);
        await Expect(Settings.NumberLine).ToHaveTextAsync("Book 001 · before Players Guide");
        await Expect(Settings.OrderWas(Players)).ToHaveTextAsync("was 001");
        await Expect(Settings.OrderWas(Codex)).ToHaveTextAsync("was 002");
        await Expect(Settings.Toast).ToContainTextAsync("Book 001");

        var undo = await Page.RunAndWaitForApiAsync($"/api/books/{Bestiary}/series-number", "PUT",
            () => Settings.ToastUndo.ClickAsync());

        undo.Status.ShouldBe(200);
        await Expect(Settings.NumberLine).ToHaveTextAsync("Book 003 · last in the series");
        await Expect(Settings.OrderWas(Players)).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task Number_WhenInvalid_ShouldBeRejected()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        foreach (var number in new[] { 0, 4 })
        {
            var response = await client.PutAsJsonAsync($"/api/books/{Bestiary}/series-number", new SetSeriesNumberRequest(number));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.ShouldBe(ApiErrorCodes.Validation);
        }

        await Page.GotoAsync(Links.Settings(Bestiary, Links.Hash(("series", null))));
        await Expect(Settings.NumberLine).ToHaveTextAsync("Book 003 · last in the series");
    }

    [Test, Order(3)]
    public async Task Move_ShouldCopyKbDown()
    {
        await Page.GotoAsync(Links.Settings(Bestiary, Links.Hash(("series", null))));
        await Settings.SeriesSelect.SelectOptionAsync(new SelectOptionValue { Label = "None (standalone)" });
        await Expect(Settings.MoveNote).ToContainTextAsync("Not moved yet");

        await Settings.MoveReview.ClickAsync();
        await Expect(Settings.MoveDialog).ToBeVisibleAsync();
        await Expect(Settings.MoveFromTo).ToContainTextAsync("Forsaken Gods · Book 003");
        await Expect(Settings.MoveStep("copyCategories")).ToBeVisibleAsync();
        await Expect(Settings.MoveStep("repoint")).ToBeVisibleAsync();
        await Expect(Settings.MoveWarning).ToContainTextAsync("won't merge");

        var moved = await Page.RunAndWaitForApiAsync($"/api/books/{Bestiary}/series-move", "POST",
            () => Settings.MoveConfirm.ClickAsync());

        moved.Status.ShouldBe(200);
        await Expect(Settings.MoveDialog).ToBeHiddenAsync();
        await Expect(Settings.SeriesSelect).ToHaveValueAsync(string.Empty);
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var kb = (await client.GetFromJsonAsync<KbDto>($"/api/books/{Bestiary}/kb"))!;
        kb.Categories.ShouldContain(c => c.Name == "Spells" && !c.FromSeries);
        kb.Entries.Count(e => e.Name.Length > 0).ShouldBeGreaterThanOrEqualTo(3);
        var rest = (await client.GetFromJsonAsync<BookSettingsDto>($"/api/books/{Codex}/settings"))!;
        rest.SeriesOrder.Select(o => o.Number).ShouldBe([1, 2]);
    }
}
