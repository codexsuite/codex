using Codex.AcceptanceTests.Pages.Cite;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Cite;

// J9 A: cite Accelerate again from Ch 7 Sc 1. Its first use is the Ch 5 Sc 2 pending draft, so it is already 8: the
// new marker is 8 in warn and nothing moves. CiteFirstUseTests and CiteRemoveTests are the scenarios that shift.
[TestFixture]
public class CiteExistingTests : CiteLaneTest
{
    [Test]
    public async Task Cite_WhenAccelerateIsCitedAgain_ShouldGiveMarker8InWarn()
    {
        var write = await OpenAsync();
        await Expect(write.Markers).ToHaveCountAsync(15);

        var result = await CiteAsync(SeedIds.References.Accelerate, 1);

        await Expect(write.Marker(result.UseId)).ToHaveAttributeAsync("data-number", "8");
        await Expect(write.Marker(result.UseId)).ToHaveAttributeAsync("data-tone", "warn");
        await Expect(write.Markers).ToHaveCountAsync(16);
        await Expect(write.Marker(SeedIds.ReferenceUses.TeamTopologiesCh9Sc1)).ToHaveAttributeAsync("data-number", "9");
        (await GetProseAsync()).Body.ShouldBe(result.Body);
    }

    [Test]
    public async Task Picker_WhenOpened_ShouldShowTonesAndOfferNewSource()
    {
        var write = await OpenAsync();
        var cite = new CitePage(Page);
        var before = (await GetProseAsync()).Body;
        await cite.SelectAsync(Scene, Sentence(before, 3));
        await Page.Keyboard.TypeAsync("/source");
        await cite.SourceReference.ClickAsync();

        await Expect(cite.Option(SeedIds.References.Accelerate)).ToHaveAttributeAsync("data-tone", "warn");
        await Expect(cite.Option(SeedIds.References.LeanSoftware)).ToHaveAttributeAsync("data-tone", "muted");
        await Expect(cite.NewSource).ToBeEnabledAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(cite.Picker).ToHaveCountAsync(0);
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
    }
}
