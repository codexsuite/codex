using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Layout;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;

namespace Codex.AcceptanceTests.Lanes.Layout;

// Proof on the seed's Players Guide chapter 1: the worker compiles it with the real template and pdf.js shows the pages;
// asking again for the same chapter is answered from the cache; an edit makes the proof out of date; a chapter that does
// not compile shows its log. Needs pandoc, tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class LayoutProofTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.PlayersGuide.ToString();
    private static readonly Guid FigureScene = SeedIds.Scenes.PlayersGuideFigureScene;
    private static readonly LocatorAssertionsToBeVisibleOptions Compile = new() { Timeout = 90_000 };
    private static readonly Regex Pages = new(@"^[1-9]\d* pages?$");

    private LayoutScreen Layout => new(Page);

    private async Task OpenProofAsync()
    {
        await Page.GotoAsync(Links.Layout(BookId));
        await Expect(Layout.StatusPages).ToHaveTextAsync(Pages);
        await Layout.ModeProof.ClickAsync();
    }

    private async Task<ProofListDto> ProofsAsync()
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await api.GetFromJsonAsync<ProofListDto>($"/api/books/{BookId}/proofs?chapterId={SeedIds.Chapters.PlayersGuideCh1}"))!;
    }

    private async Task AppendToFigureSceneAsync(string text)
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var current = (await api.GetFromJsonAsync<ProseDto>($"/api/scenes/{FigureScene}/prose"))!;
        using var saved = await api.PutAsJsonAsync($"/api/scenes/{FigureScene}/prose", new SaveProseRequest($"{current.Body}\n\n{text}", current.Version));
        saved.EnsureSuccessStatusCode();
    }

    [Test, Order(1)]
    public async Task Proof_WhenChosen_ShouldCompileTheChapterAndShowItsPagesInThePdfViewer()
    {
        await OpenProofAsync();

        await Expect(Layout.ProofPage(1)).ToHaveAttributeAsync("data-rendered", "true", new() { Timeout = 90_000 });
        await Expect(Layout.ProofBanner).ToContainTextAsync("chapter only, no contents or cross-references");
        await Expect(Layout.ProofBanner).ToContainTextAsync("novel-6x9 v1.0.0");
        await Expect(Layout.StatusPages).ToHaveTextAsync(Pages);
        await Expect(Layout.ProofDownload).ToHaveAttributeAsync("href", new Regex(@"^/api/proofs/[0-9a-f-]+/pdf$"));
        await Expect(Layout.ProofCached).ToHaveCountAsync(0);
        (await ProofsAsync()).Proofs.Count.ShouldBe(1);
    }

    [Test, Order(2)]
    public async Task Proof_WhenAskedForAgainWithNothingChanged_ShouldBeCachedAndRerenderMakesANewOne()
    {
        await OpenProofAsync();

        await Expect(Layout.ProofCached).ToBeVisibleAsync(Compile);
        await Expect(Layout.ProofPage(1)).ToHaveAttributeAsync("data-rendered", "true", new() { Timeout = 30_000 });
        (await ProofsAsync()).Proofs.Count.ShouldBe(1);

        await Layout.ProofRerender.ClickAsync();
        await Expect(Layout.ProofCached).ToHaveCountAsync(0, new() { Timeout = 90_000 });
        await Expect(Layout.ProofPage(1)).ToHaveAttributeAsync("data-rendered", "true", new() { Timeout = 90_000 });
        (await ProofsAsync()).Proofs.Count.ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Proof_WhenTheChapterChangedSinceTheProof_ShouldShowTheOldOneAsOutOfDate()
    {
        await AppendToFigureSceneAsync("The proof is out of date now.");

        await OpenProofAsync();

        await Expect(Layout.ProofOutOfDate).ToBeVisibleAsync(Compile);
        await Expect(Layout.ProofPage(1)).ToHaveAttributeAsync("data-rendered", "true", new() { Timeout = 30_000 });
        (await ProofsAsync()).Proofs.Count.ShouldBe(2);
    }

    [Test, Order(4)]
    public async Task Proof_WhenTheChapterDoesNotCompile_ShouldShowTheCompileLog()
    {
        await AppendToFigureSceneAsync("```{=latex}\n\\codexUndefinedMacro\n```");
        await OpenProofAsync();
        await Expect(Layout.ProofOutOfDate).ToBeVisibleAsync(Compile);

        await Layout.ProofRerender.ClickAsync();

        await Expect(Layout.ProofError).ToBeVisibleAsync(Compile);
        await Expect(Layout.ProofLog).ToContainTextAsync("codexUndefinedMacro");
        await Expect(Layout.ProofPages).ToHaveCountAsync(0);
        await Layout.ProofLogToggle.ClickAsync();
        await Expect(Layout.ProofLog).ToBeHiddenAsync();
    }
}
