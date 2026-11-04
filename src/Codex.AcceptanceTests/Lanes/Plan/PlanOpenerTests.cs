using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Images;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;
using Codex.Contracts.Structure;

namespace Codex.AcceptanceTests.Lanes.Plan;

// The Players Guide's first act gets the book's figure image as its part-opener art from the act's ⋯ → Opener art…;
// the outline carries it and the act shows the thumbnail after a reload.
[TestFixture]
public class PlanOpenerTests : SeededAcceptanceTest
{
    private static readonly Guid Book = SeedIds.Books.PlayersGuide;
    private static readonly Guid Act = SeedIds.Acts.PlayersGuideAct1;
    private static readonly Guid Asset = SeedIds.Assets.PlayersGuideFigure;

    [Test, Order(1)]
    public async Task ActOpener_WhenSetFromTheActMenu_ShouldSaveAndShowTheThumbnail()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(Book.ToString()));

        await plan.ActMore(Act).ClickAsync();
        await plan.ActOpener(Act).ClickAsync();
        await Expect(plan.OpenerDialog).ToBeVisibleAsync();
        await Page.GetByTestId(ImagesTestIds.Option(Asset)).ClickAsync();
        // An opener prints as a full page, so the dpi is judged at that size.
        await Expect(Page.GetByTestId(ImagesTestIds.SelectedDpi)).ToContainTextAsync("full page");
        await plan.OpenerCaption.FillAsync("The Fallen World");
        var saved = await Page.RunAndWaitForApiAsync($"/api/acts/{Act}/opener", "PUT", () => plan.OpenerSave.ClickAsync());

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.OpenerDialog).ToHaveCountAsync(0);
        await Expect(plan.ActOpenerThumb(Act)).ToBeVisibleAsync();
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var outline = (await client.GetFromJsonAsync<OutlineDto>($"/api/books/{Book}/outline"))!;
        var opener = outline.Acts.Single(a => a.Id == Act).Opener!;
        (opener.AssetId, opener.Caption).ShouldBe((Asset, "The Fallen World"));

        await Page.ReloadAsync();
        await Expect(plan.ActOpenerThumb(Act)).ToHaveAttributeAsync("title", "Opener art: The Fallen World");
    }
}
