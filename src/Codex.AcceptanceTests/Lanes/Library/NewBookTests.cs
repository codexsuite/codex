using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// From the seed: a standalone book lands on its Plan URL, a book added from a series screen pre-fills the series and
// the next number, and an empty title is refused without a request.
[TestFixture]
public class NewBookTests : SeededAcceptanceTest
{
    private NewBookDialog Dialog => new(Page);

    [Test, Order(1)]
    public async Task NewBook_WhenStandalone_ShouldLandOnItsPlanUrl()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Dialog.OpenButton).ToBeVisibleAsync();
        await Dialog.OpenButton.ClickAsync();
        await Expect(Dialog.Root).ToBeVisibleAsync();
        await Dialog.Title.FillAsync("The Lantern Year");

        var response = await Page.RunAndWaitForApiAsync("/api/books", "POST", () => Dialog.Create.ClickAsync());

        response.Status.ShouldBe(201);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/books/[0-9a-f-]{36}/plan$"));
    }

    [Test, Order(2)]
    public async Task NewBook_FromSeries_ShouldPrefillSeriesAndNextNumber()
    {
        var series = SeedIds.Series.ForsakenGods.ToString();
        await Page.GotoAsync(Links.Series(series));

        await new SeriesScreen(Page).AddBook.ClickAsync();

        await Expect(Dialog.Root).ToBeVisibleAsync();
        await Expect(Dialog.Series).ToHaveValueAsync(series);
        await Expect(Dialog.Number).ToHaveValueAsync("4");
    }

    [Test, Order(3)]
    public async Task NewBook_WhenTitleEmpty_ShouldBeRejected()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Dialog.OpenButton).ToBeVisibleAsync();
        await Dialog.OpenButton.ClickAsync();
        await Expect(Dialog.Root).ToBeVisibleAsync();
        var posted = false;
        Page.Request += (_, request) => posted |= request.Method == "POST" && request.Url.EndsWith("/api/books");

        await Dialog.Create.ClickAsync();

        await Expect(Dialog.TitleError).ToBeVisibleAsync();
        await Expect(Dialog.Root).ToBeVisibleAsync();
        posted.ShouldBeFalse();
    }
}
