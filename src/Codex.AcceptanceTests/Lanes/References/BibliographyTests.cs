using Codex.AcceptanceTests.Harness;
using Codex.Contracts;
using Codex.AcceptanceTests.Pages.References;

namespace Codex.AcceptanceTests.Lanes.References;

// The bibliography view and the four exports over the seeded Product Engineering book.
[TestFixture]
public class BibliographyTests : SeededAcceptanceTest
{
    private static string Book => SeedIds.Books.ProductEngineering.ToString();

    [Test, Order(1)]
    public async Task Bibliography_ShouldOmitUncited()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        await screen.ViewBibliography.ClickAsync();

        await Expect(screen.BibliographyEntries).ToHaveCountAsync(11);
        await Expect(screen.BibliographyEntries.Filter(new() { HasText = "Lean Software Development" })).ToHaveCountAsync(0);
        await Expect(screen.BibliographyEntries.Filter(new() { HasText = "The Velocity Trap" })).ToHaveCountAsync(1);
    }

    [Test, Order(2)]
    public async Task Bibliography_WhenStyleChanged_ShouldReformat()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);
        await screen.ViewBibliography.ClickAsync();
        await Expect(screen.BibliographyEntries).ToHaveCountAsync(11);
        var chicago = await screen.BibliographyEntries.AllInnerTextsAsync();

        await Page.GetByTestId(ReferencesTestIds.BibStyle).SelectOptionAsync("apa");

        await Expect(screen.BibliographyEntries).ToHaveCountAsync(11);
        var apa = await screen.BibliographyEntries.AllInnerTextsAsync();
        apa.ShouldNotBe(chicago);
    }

    [TestCase("bibtex", ".bib", "@book{")]
    [TestCase("ris", ".ris", "ER  - ")]
    [TestCase("csljson", ".json", "\"title\": \"The Velocity Trap\"")]
    [TestCase("permissions", ".csv", "Source,Authors,Year")]
    public async Task Exports_ShouldDownload(string format, string extension, string expectedContent)
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        var download = await screen.ExportAsync(format);

        download.SuggestedFilename.ShouldEndWith(extension);
        using var reader = new StreamReader(await download.CreateReadStreamAsync());
        (await reader.ReadToEndAsync()).ShouldContain(expectedContent);
    }
}
