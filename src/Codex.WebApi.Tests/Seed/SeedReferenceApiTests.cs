using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Books;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Seed;

// The reference endpoints read the seeded Product Engineering book and agree with issue #16 section 1.6.
[TestFixture]
public class SeedReferenceApiTests : ApiFixture
{
    [Test]
    public async Task Summary_WhenSeeded_ShouldBeFourOfElevenClearedWithThreeRisksAndBadgeThree()
    {
        var summary = (await Client.GetFromJsonAsync<ClearanceSummaryDto>(
            $"/api/books/{SeedIds.Books.ProductEngineering}/references/summary"))!;

        // Total counts every live source, including the uncited Poppendieck; the strip's "of 11" is Total - NotCited.
        summary.ShouldBe(new ClearanceSummaryDto(Cleared: 4, Total: 12, Risks: 3, NeedsCheck: 4, NotCited: 1, Badge: 3, RemindersDue: 0));
        (summary.Total - summary.NotCited).ShouldBe(11);
    }

    [Test]
    public async Task BookHeader_WhenSeeded_ShouldBadgeReferencesWithThreeRisksInDanger()
    {
        var header = (await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{SeedIds.Books.ProductEngineering}"))!;

        (header.ReferenceBadgeCount, header.ReferenceBadgeTone).ShouldBe((3, "danger"));
    }

    [Test]
    public async Task Markers_WhenSeeded_ShouldNumberTheElevenSourcesInIssue16Order()
    {
        var markers = (await Client.GetFromJsonAsync<MarkerDto[]>(
            $"/api/books/{SeedIds.Books.ProductEngineering}/markers"))!;

        markers.Length.ShouldBe(15);
        var firstSeen = markers.GroupBy(m => m.Number).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.First().SourceTitle[..6], g.First().Status)).ToArray();
        firstSeen.ShouldBe(
        [
            (1, "Extrem", "cleared"), (2, "Manife", "needs_check"), (3, "Proble", "needs_check"), (4, "The Ve", "risk"),
            (5, "How Do", "cleared"), (6, "Empowe", "cleared"), (7, "2024 S", "needs_check"), (8, "Accele", "needs_check"),
            (9, "Team T", "risk"), (10, "The My", "cleared"), (11, "Ship I", "risk"),
        ]);
        markers.Where(m => m.GenerationId != null).Select(m => m.UseId).ShouldBe(
            [SeedIds.ReferenceUses.GoodhartMonetaryCh5Sc2, SeedIds.ReferenceUses.DoraReport2024Ch5Sc2, SeedIds.ReferenceUses.AccelerateCh5Sc2],
            ignoreOrder: true);
    }
}
