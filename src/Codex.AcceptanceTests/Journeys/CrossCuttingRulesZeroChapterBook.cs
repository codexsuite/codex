using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// Cross-cutting rule 6 (the 0-chapter book): Northbound opens on Plan, Write and References with their empty states,
// no failed request and no uncaught script error.
[TestFixture]
[Category("CrossCuttingRules")]
public class CrossCuttingRulesZeroChapterBook : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.Northbound.ToString();
    private readonly List<string> _problems = [];

    [SetUp]
    public void WatchForProblems()
    {
        _problems.Clear();
        Page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                _problems.Add($"{response.Status} {response.Request.Method} {response.Url}");
            }
        };
        Page.PageError += (_, message) => _problems.Add("script error: " + message);
    }

    [Test, Order(1)]
    public async Task Plan_WhenTheBookHasNoChapters_ShouldShowTheEmptyActWithoutErrors()
    {
        var plan = new PlanScreen(Page);

        await Page.GotoAsync(Links.Plan(BookId));

        await Expect(new AppShell(Page).BookBar.Root).ToBeVisibleAsync();
        await Expect(plan.ActMeta(SeedIds.Acts.NorthboundUntitled)).ToHaveTextAsync("No chapters yet.");
        await Expect(plan.LoadError).ToHaveCountAsync(0);
        _problems.ShouldBeEmpty();
    }

    [Test, Order(2)]
    public async Task Write_WhenTheBookHasNoChapters_ShouldSayThereAreNoneWithoutErrors()
    {
        await Page.GotoAsync(Links.Write(BookId));

        await Expect(Page.GetByTestId(WriteTestIds.Empty)).ToContainTextAsync("no chapters yet");
        _problems.ShouldBeEmpty();
    }

    [Test, Order(3)]
    public async Task References_WhenTheBookHasNoChapters_ShouldShowAnEmptyTableWithoutErrors()
    {
        var references = new ReferencesScreen(Page);

        await Page.GotoAsync(Links.References(BookId));

        await Expect(references.Root).ToBeVisibleAsync();
        await Expect(references.Rows).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId(ReferencesTestIds.Empty)).ToBeVisibleAsync();
        _problems.ShouldBeEmpty();
    }
}
