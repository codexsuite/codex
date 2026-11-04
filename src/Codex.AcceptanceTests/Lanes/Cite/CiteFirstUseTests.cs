using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Cite;

// J9 A, the case that shifts: Lean Software Development is in References but not cited, so its first marker (in
// Ch 7 Sc 1) takes 9 and every later source moves up one.
[TestFixture]
public class CiteFirstUseTests : CiteLaneTest
{
    [Test]
    public async Task Cite_WhenAnUncitedSourceIsCited_ShouldMoveLaterMarkersUp()
    {
        var write = await OpenAsync();
        await Expect(write.Markers).ToHaveCountAsync(15);

        var result = await CiteAsync(SeedIds.References.LeanSoftware, 2);

        await Expect(write.Marker(result.UseId)).ToHaveAttributeAsync("data-number", "9");
        await Expect(write.Marker(SeedIds.ReferenceUses.AccelerateCh7Sc1)).ToHaveAttributeAsync("data-number", "8");
        await Expect(write.Marker(SeedIds.ReferenceUses.TeamTopologiesCh9Sc1)).ToHaveAttributeAsync("data-number", "10");
        await Expect(write.Marker(SeedIds.ReferenceUses.MythicalManMonthCh9Sc1)).ToHaveAttributeAsync("data-number", "11");
        await Expect(write.Marker(SeedIds.ReferenceUses.ShipItFridayCh10Sc1)).ToHaveAttributeAsync("data-number", "12");
    }
}
