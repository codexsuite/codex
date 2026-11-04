using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Ai;

// Nothing connected: the Write draft panel says so and links to Settings → AI instead of offering Generate.
[TestFixture]
public class NoProviderConnectedTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Beat = SeedIds.Beats.Ch3Sc3BadScrum;

    [Test]
    public async Task Write_WhenNoProviderConnected_ShouldLinkToSettingsAndOfferNoGenerate()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));

        // Every beat card carries the link; the first stands for them all.
        await Expect(write.DraftConnect.First).ToContainTextAsync("Connect an AI provider in Settings");
        await Expect(write.DraftConnect.First).ToHaveAttributeAsync("href", "/settings/ai");
        await Expect(write.BeatGenerate(Beat)).ToHaveCountAsync(0);
    }
}
