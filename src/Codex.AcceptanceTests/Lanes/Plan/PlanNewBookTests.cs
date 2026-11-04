using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Northbound is a book with no chapters yet: the act the insert trigger created, and nothing else.
[TestFixture]
public class PlanNewBookTests : SeededAcceptanceTest
{
    [Test, Order(1)]
    public async Task Northbound_ShouldShowOneEmptyActAndNewChapter()
    {
        var bookId = SeedIds.Books.Northbound;
        var plan = new PlanScreen(Page);

        await Page.GotoAsync(Links.Plan(bookId.ToString()));

        await Expect(plan.Act(SeedIds.Acts.NorthboundUntitled)).ToContainTextAsync("Act 1");
        await Expect(plan.ActMeta(SeedIds.Acts.NorthboundUntitled)).ToHaveTextAsync("No chapters yet.");
        await Expect(plan.NewChapter(SeedIds.Acts.NorthboundUntitled)).ToHaveTextAsync("New Chapter");
        await Expect(plan.ChapterHeadings).ToHaveCountAsync(0);
        await Expect(plan.SceneNumbers).ToHaveCountAsync(0);

        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/books/{bookId}/outline");
        using var outline = JsonDocument.Parse(await response.TextAsync());
        outline.RootElement.GetProperty("acts").GetArrayLength().ShouldBe(1);
    }
}
