using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.References;

// Add source over the fake lookup: add by ISBN, meet the same ISBN again, then a DOI nobody knows falls back to the fields.
[TestFixture]
public class AddSourceTests : ReferencesLaneTest
{
    private const string Isbn = "978-0-14-044913-6";

    private static string NewLink => Links.References(Book, Links.Hash(("new", null)));

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    [Test, Order(1)]
    public async Task Isbn_WhenNewToTheBook_ShouldBeAddedFromTheLookupAndOpened()
    {
        await Page.GotoAsync(NewLink);
        var form = new AddSourceForm(Page);

        await form.Input.FillAsync(Isbn);
        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references", "POST", () => form.Lookup.ClickAsync());

        response.Status.ShouldBe(201);
        await Expect(form.Form).ToHaveCountAsync(0);
        await Expect(new SourcePanel(Page).Title).ToHaveTextAsync("The Histories");
        await Expect(new SourcePanel(Page).Field("isbn")).ToHaveValueAsync("9780140449136");
    }

    [Test, Order(2)]
    public async Task Isbn_WhenMatchingExisting_ShouldOfferIt()
    {
        await Page.GotoAsync(NewLink);
        var form = new AddSourceForm(Page);

        await form.Input.FillAsync(Isbn);
        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references/lookup", "POST", () => form.Lookup.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(form.MatchTitle).ToHaveTextAsync("The Histories");
        await Expect(form.Lookup).ToHaveCountAsync(0);
        await Expect(form.UseExisting).ToContainTextAsync("Use existing source: The Histories");
        await form.UseExisting.ClickAsync();
        await Expect(form.Form).ToHaveCountAsync(0);
        await Expect(new SourcePanel(Page).Title).ToHaveTextAsync("The Histories");
    }

    [Test, Order(3)]
    public async Task Lookup_WhenNotFound_ShouldFallBackToManual()
    {
        await Page.GotoAsync(NewLink);
        var form = new AddSourceForm(Page);

        await form.Input.FillAsync("10.9999/nobody-knows");
        await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references/lookup", "POST", () => form.Lookup.ClickAsync());

        await Expect(form.NotFound).ToBeVisibleAsync();
        await Expect(form.Field("doi")).ToHaveValueAsync("10.9999/nobody-knows");
        await Expect(form.Save).ToBeDisabledAsync();
        await form.Field("title").FillAsync("A paper typed by hand");
        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references", "POST", () => form.Save.ClickAsync());

        response.Status.ShouldBe(201);
        await Expect(new SourcePanel(Page).Title).ToHaveTextAsync("A paper typed by hand");
    }

    [Test, Order(4)]
    public async Task Lookup_WhenTheTextIsNotAnIdentifier_ShouldSayWhyAndSendNothing()
    {
        await Page.GotoAsync(NewLink);
        var form = new AddSourceForm(Page);

        await form.Input.FillAsync("The Lean Startup");
        await form.Lookup.ClickAsync();

        await Expect(form.Error).ToHaveTextAsync("Enter an ISBN, DOI or web address.");
        await Expect(form.Form).ToBeVisibleAsync();
    }
}
