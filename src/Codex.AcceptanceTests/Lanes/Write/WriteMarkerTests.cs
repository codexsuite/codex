using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Write;

// Source markers in the manuscript, from the seed: 15 markers on 11 sources (numbers shared per source, in order of
// first use), three of them in the pending draft at Ch 5 Sc 2, and the card a marker opens.
[TestFixture]
public class WriteMarkerTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    // Each use's source number and tone (S1b's per-source table; numbers are shared by a source's uses).
    private static readonly (Guid Use, int Number, string Tone)[] Expected =
    [
        (SeedIds.ReferenceUses.BeckXpCh3Sc1, 1, "ok"),
        (SeedIds.ReferenceUses.AgileManifestoWebCh3Sc2, 2, "warn"),
        (SeedIds.ReferenceUses.GoodhartMonetaryCh4Sc1, 3, "warn"),
        (SeedIds.ReferenceUses.GoodhartMonetaryCh5Sc2, 3, "warn"),
        (SeedIds.ReferenceUses.VelocityTrapCh4Sc1, 4, "danger"),
        (SeedIds.ReferenceUses.VelocityTrapCh8Sc1, 4, "danger"),
        (SeedIds.ReferenceUses.ConwayCommitteesCh4Sc2, 5, "ok"),
        (SeedIds.ReferenceUses.ConwayCommitteesCh9Sc1, 5, "ok"),
        (SeedIds.ReferenceUses.CaganEmpoweredCh5Sc1, 6, "ok"),
        (SeedIds.ReferenceUses.DoraReport2024Ch5Sc2, 7, "warn"),
        (SeedIds.ReferenceUses.AccelerateCh5Sc2, 8, "warn"),
        (SeedIds.ReferenceUses.AccelerateCh7Sc1, 8, "warn"),
        (SeedIds.ReferenceUses.TeamTopologiesCh9Sc1, 9, "danger"),
        (SeedIds.ReferenceUses.MythicalManMonthCh9Sc1, 10, "ok"),
        (SeedIds.ReferenceUses.ShipItFridayCh10Sc1, 11, "danger"),
    ];

    [Test, Order(1)]
    public async Task Markers_ShouldMatchSeedNumbersAndColours()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Markers).ToHaveCountAsync(15);

        foreach (var (use, number, tone) in Expected)
        {
            await Expect(write.Marker(use)).ToHaveAttributeAsync("data-number", number.ToString());
            await Expect(write.Marker(use)).ToHaveAttributeAsync("data-tone", tone);
        }
    }

    [Test, Order(2)]
    public async Task MarkerCard_ShouldLinkToReferences()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Markers).ToHaveCountAsync(15);

        await write.Marker(SeedIds.ReferenceUses.VelocityTrapCh4Sc1).ClickAsync();

        await Expect(write.MarkerCardSource).ToContainTextAsync("Velocity Trap");
        await Expect(write.MarkerCardStatus).ToHaveTextAsync("Copyright risk");
        await Expect(write.MarkerCardReason).ToHaveTextAsync("Quote over limit");
        await Expect(write.MarkerCardWords).ToHaveTextAsync("342 of 300 quoted words");
        await Expect(write.MarkerCardOpen).ToHaveAttributeAsync(
            "href", $"/books/{BookId}/references#{SeedIds.References.VelocityTrap}");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(write.MarkerCard).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task PendingDraft_ShouldCarryMarker()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("ref", SeedIds.References.Accelerate.ToString()))));

        var inDraft = write.DraftText(SeedIds.Beats.Ch5Sc2OutcomeMetrics).GetByTestId(WriteTestIds.Marker(SeedIds.ReferenceUses.AccelerateCh5Sc2));
        await Expect(inDraft).ToHaveCountAsync(1);
        await Expect(inDraft).ToHaveAttributeAsync("data-number", "8");
        await Expect(inDraft).ToHaveAttributeAsync("data-tone", "warn");
        // The deep link opens the card of the source's first marker (Accelerate's first use is in the pending draft).
        await Expect(write.MarkerCardSource).ToContainTextAsync("Accelerate");
        await Expect(write.MarkerCard).ToHaveAttributeAsync("data-use", SeedIds.ReferenceUses.AccelerateCh5Sc2.ToString());
    }
}
