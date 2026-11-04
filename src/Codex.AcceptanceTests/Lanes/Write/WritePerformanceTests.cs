using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Structure;

namespace Codex.AcceptanceTests.Lanes.Write;

// Write and its minimap on a book of real size: Iron Litany from db/seed-large, 124,870 words in 3 acts, 30 chapters
// and 90 scenes. Times come from the browser's own clock (performance.now() since navigation start, or a keydown
// handler's own timestamps); a test waits on locators and never sleeps. The budgets are about twice the worst of three
// local runs (this host, Chromium headless, one test at a time), rounded up, because CI runners are slower
// and run shards in parallel.
[TestFixture]
public class WritePerformanceTests : SeededLargeAcceptanceTest
{
    private const int Keystrokes = 50;

    // Measured locally over 3 runs: 2891, 2034, 5299 ms (worst 5299; a 2 s main-thread task mounts all 90 editors: a finding)
    private const double FirstSceneBudgetMs = 11000;

    // Measured locally over 3 runs: 2520, 1861, 4439 ms
    private const double DeepLinkBudgetMs = 9000;

    // Measured locally over 3 runs: 29, 29, 28 ms
    private const double KeystrokeMedianBudgetMs = 60;

    // Measured locally over 3 runs: 38, 42, 37 ms. One slow keystroke out of 50 on a CI runner took 100 ms (PR #38,
    // median still 30 ms), so the budget is half as much again as that; a per-keystroke regression moves the median.
    private const double KeystrokeWorstBudgetMs = 150;

    // Measured locally over 3 runs: 2437, 2919, 2898 ms (includes the 3 s autosave debounce)
    private const double SavedBudgetMs = 6000;

    private static readonly string BookId = SeedIds.Books.IronLitany.ToString();

    private List<Guid> _sceneIds = [];

    [OneTimeSetUp]
    public async Task ReadOutlineAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var outline = (await client.GetFromJsonAsync<OutlineDto>($"/api/books/{BookId}/outline"))!;
        _sceneIds = outline.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Select(s => s.Id).ToList();
        _sceneIds.Count.ShouldBe(90);
    }

    private static void Report(string name, double value) => TestContext.Out.WriteLine($"PERF {name} = {value:F0} ms");

    private Task<double> BrowserNowAsync() => Page.EvaluateAsync<double>("() => performance.now()");

    [Test, Order(1)]
    public async Task Minimap_WhenLargeBook_ShouldHaveATickPerSceneAndAMarkPerChapter()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId));

        await Expect(write.MinimapTicks).ToHaveCountAsync(90);
        await Expect(write.MinimapChapters).ToHaveCountAsync(30);
    }

    [Test, Order(2)]
    public async Task Open_WhenLargeBook_ShouldShowTheMinimapAndFirstSceneWithinBudget()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.MinimapTicks).ToHaveCountAsync(90);
        await Expect(write.Scene(_sceneIds[0])).ToBeInViewportAsync();
        await Expect(write.ProseEditor(_sceneIds[0])).ToBeVisibleAsync();
        var elapsed = await BrowserNowAsync();

        Report("first-scene", elapsed);
        elapsed.ShouldBeLessThan(FirstSceneBudgetMs);
    }

    [Test, Order(3)]
    public async Task Hash_WhenLastScene_ShouldScrollAndFlashWithinBudget()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "30.3"))));
        await Expect(write.Scene(_sceneIds[^1])).ToHaveAttributeAsync("data-flash", "true");
        await Expect(write.Scene(_sceneIds[^1])).ToBeInViewportAsync();
        var elapsed = await BrowserNowAsync();

        Report("deep-link-last-scene", elapsed);
        elapsed.ShouldBeLessThan(DeepLinkBudgetMs);
        (await write.ScrollTopAsync()).ShouldBeGreaterThan(0);
        await Expect(write.Scene(_sceneIds[0])).Not.ToBeInViewportAsync();
    }

    [Test, Order(4)]
    public async Task Typing_WhenMidBookScene_ShouldKeepKeystrokesFastAndAutosave()
    {
        var write = new WritePageObject(Page);
        var sceneId = _sceneIds[44];
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "15.3"))));
        await Expect(write.ProseEditor(sceneId)).ToBeVisibleAsync();
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await write.ProseEditor(sceneId).ClickAsync();
        // Each keydown records the time until the second animation frame after it: the input is handled and painted.
        await Page.EvaluateAsync(
            """
            () => {
              window.__keystrokes = [];
              document.addEventListener('keydown', () => {
                const start = performance.now();
                requestAnimationFrame(() => requestAnimationFrame(() => window.__keystrokes.push(performance.now() - start)));
              }, true);
            }
            """);

        await Page.Keyboard.TypeAsync(new string('x', Keystrokes));
        await Page.WaitForFunctionAsync($"() => window.__keystrokes.length >= {Keystrokes}");
        var typedAt = await BrowserNowAsync();
        var latencies = (await Page.EvaluateAsync<JsonElement>("() => window.__keystrokes"))
            .EnumerateArray().Select(e => e.GetDouble()).Order().ToList();
        var median = latencies[latencies.Count / 2];
        var worst = latencies[^1];
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 20000 });
        var savedAfter = await BrowserNowAsync() - typedAt;

        Report("keystroke-median", median);
        Report("keystroke-worst", worst);
        Report("typed-to-saved", savedAfter);
        median.ShouldBeLessThan(KeystrokeMedianBudgetMs);
        worst.ShouldBeLessThan(KeystrokeWorstBudgetMs);
        savedAfter.ShouldBeLessThan(SavedBudgetMs);
        var saved = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @id", ("id", sceneId));
        saved!.ShouldContain(new string('x', Keystrokes));
    }

    private async Task<T?> ScalarSqlAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Clone.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }
}
