using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Settings;

namespace Codex.AcceptanceTests.Lanes.Settings;

// Citations & permissions on Product Engineering (Chicago, 300 words, Velocity Trap quotes 342; three risks in the seed).
// The clearance summary carries no citation style, so the style is asserted through the settings' own GET; the limit's
// effect is asserted through GET …/references/summary.
[TestFixture]
public class SettingsCitationTests : SeededAcceptanceTest
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    private SettingsScreen Settings => new(Page);

    private async Task<T> GetAsync<T>(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<T>(path))!;
    }

    [Test, Order(1)]
    public async Task Style_WhenApaSaved_ShouldPersist()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("citations", null))));
        await Expect(Settings.CitationStyleOption("chicago")).ToHaveAttributeAsync("aria-pressed", "true");

        var saved = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/publication-settings", "PATCH",
            () => Settings.CitationStyleOption("apa").ClickAsync());

        saved.Status.ShouldBe(200);
        await Expect(Settings.CitationStyleOption("apa")).ToHaveAttributeAsync("aria-pressed", "true");
        (await GetAsync<PublicationSettingsDto>($"/api/books/{Book}/publication-settings")).Style.Value.ShouldBe("apa");
        await Page.ReloadAsync();
        await Expect(Settings.CitationStyleOption("apa")).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Test, Order(2)]
    public async Task Limit_WhenZero_ShouldBeRefused()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("rules", null))));
        await Settings.QuoteLimit.FillAsync("0");
        await Settings.QuoteLimit.BlurAsync();

        await Expect(Settings.QuoteLimitError).ToBeVisibleAsync();
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await client.PatchAsJsonAsync($"/api/books/{Book}/publication-settings",
            new PatchPublicationSettingsRequest(null, 0, null, null, null, null, null));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetAsync<ClearanceSummaryDto>($"/api/books/{Book}/references/summary")).Risks.ShouldBe(3);
    }

    [Test, Order(3)]
    public async Task Limit_WhenLowered_ShouldRaiseRisks()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("rules", null))));
        await Expect(Settings.QuoteLimit).ToBeFocusedAsync();
        await Expect(Settings.ImpactSummary).ToContainTextAsync("3 sources at copyright risk");

        await Settings.QuoteLimit.FillAsync("15");
        await Expect(Settings.ImpactSummary).ToContainTextAsync("4 sources at copyright risk");
        var saved = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/publication-settings", "PATCH",
            () => Settings.QuoteLimit.BlurAsync());

        saved.Status.ShouldBe(200);
        var summary = await GetAsync<ClearanceSummaryDto>($"/api/books/{Book}/references/summary");
        summary.Risks.ShouldBe(4);
        await Expect(new Pages.Shell.BookBar(Page).ReferencesBadge).ToHaveTextAsync("4");
        (await GetAsync<PublicationSettingsDto>($"/api/books/{Book}/publication-settings")).QuoteWordLimit.Value.ShouldBe(15);
    }
}
