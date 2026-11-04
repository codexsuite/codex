using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.AcceptanceTests.Lanes.Kb;

// The entry window on Plan, from the seed. Steps run in order on one cloned database. The seed has one timeline
// note (Goodhart's Law, scene 4.1); setup adds a second one earlier in the book so order can be seen.
[TestFixture]
public class KbWindowTests : SeededAcceptanceTest
{
    private static readonly Guid EarlyNoteId = new("0199c0de-0000-7000-8000-000800000f01");

    private static string BookId => SeedIds.Books.ProductEngineering.ToString();

    private KbEntryWindow Window => new(Page);

    private KbSidebar Sidebar => new(Page);

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        var database = await base.CloneDatabaseAsync();
        await using var db = await database.OpenAsync();
        await db.ExecuteAsync(
            """
            INSERT INTO kb_progression (id, kb_entry_id, scene_id, body)
            VALUES (@id, @entry, @scene, 'An early hint of the law.');
            UPDATE prose SET body = rtrim(body, E'\n') || E'\n\n::kb-addition{id="' || @id::text || E'"}\n' WHERE scene_id = @scene;
            """,
            new { id = EarlyNoteId, entry = SeedIds.KbEntries.GoodhartsLaw, scene = SeedIds.Scenes.Ch3Sc1 });
        return database;
    }

    private static string EntryUrl(Guid entryId) => Links.Plan(BookId, Links.Hash(("kb", entryId.ToString()), ("entry", entryId.ToString())));

    [Test, Order(1)]
    public async Task UsedIn_ShouldListSeedScenes()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Expect(Window.Title).ToHaveTextAsync("Kent Beck");

        await Window.Tab("used-in").ClickAsync();

        await Expect(Window.UseRow(SeedIds.Scenes.Ch3Sc1)).ToContainTextAsync("Scene 3.1");
        await Expect(Window.UseKind(SeedIds.Scenes.Ch3Sc1, "mention")).ToHaveTextAsync("Mention");
        await Expect(Window.UseRow(SeedIds.Scenes.Ch3Sc1)).ToContainTextAsync("Kent Beck");
        await Expect(Window.OpenInWrite(SeedIds.Scenes.Ch3Sc1))
            .ToHaveAttributeAsync("href", Links.Write(BookId, Links.Hash(("scene", "3.1"))));
        await Expect(Window.HighlightInWrite)
            .ToHaveAttributeAsync("href", Links.Write(BookId, Links.Hash(("kb", SeedIds.KbEntries.KentBeck.ToString()))));
    }

    [Test, Order(2)]
    public async Task Timeline_ShouldBeInBookOrder()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.GoodhartsLaw));
        await Expect(Window.Title).ToHaveTextAsync("Goodhart's Law");

        await Window.Tab("timeline").ClickAsync();

        await Expect(Window.TimelineNotes).ToHaveCountAsync(2);
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync("From scene 3.1");
        await Expect(Window.TimelineNotes.Nth(0)).ToContainTextAsync("An early hint of the law.");
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync("From scene 4.1");
        await Expect(Window.TimelineNotes.Nth(1)).ToContainTextAsync("first time here");
    }

    [Test, Order(3)]
    public async Task Alias_WhenColliding_ShouldBeRejected()
    {
        var entryId = SeedIds.KbEntries.ConwaysLaw;
        await Page.GotoAsync(EntryUrl(entryId));
        await Expect(Window.Title).ToHaveTextAsync("Conway's Law");

        var rejected = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{entryId}", "PATCH", async () =>
        {
            await Window.AddAlias.ClickAsync();
            await Window.AliasInput.FillAsync("Goodhart's Law");
            await Window.AliasInput.PressAsync("Enter");
        });

        rejected.Status.ShouldBe(409);
        await Expect(Window.AliasError).ToContainTextAsync("Goodhart's Law");
        await Expect(Window.Alias("Goodhart's Law")).ToHaveCountAsync(0);

        var accepted = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{entryId}", "PATCH", async () =>
        {
            await Window.AliasInput.FillAsync("Conway");
            await Window.AliasInput.PressAsync("Enter");
        });

        accepted.Status.ShouldBe(200);
        await Expect(Window.Alias("Conway")).ToBeVisibleAsync();
        await Expect(Window.AliasError).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(Window.Alias("Conway")).ToBeVisibleAsync();
        await Expect(Window.Alias("Goodhart's Law")).ToHaveCountAsync(0);
    }

    [Test, Order(4)]
    public async Task NewEntry_ShouldAppearInCategory()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws)).ToContainTextAsync("Laws");

        await Window.NewEntryButton(SeedIds.KbCategories.Laws).ClickAsync();
        await Expect(Window.DraftName).ToBeFocusedAsync();
        var created = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb/entries", "POST", async () =>
        {
            await Window.DraftName.FillAsync("Campbell's Law");
            await Window.DraftName.PressAsync("Enter");
        });

        created.Status.ShouldBe(201);
        await Expect(Window.Name).ToHaveValueAsync("Campbell's Law");
        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws)).ToContainTextAsync("Campbell's Law");
        await Expect(Window.Position).ToHaveTextAsync(new Regex(@"^4 of 4$"));
        await Expect(Page).ToHaveURLAsync(new Regex(@"#kb=[^&]+&entry=[^&]+$"));

        var saved = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{await EntryIdFromUrlAsync()}", "PATCH", async () =>
        {
            await Window.Summary.FillAsync("Measure a thing and it bends.");
            await Window.Name.FocusAsync();
        });
        saved.Status.ShouldBe(200);
        await Page.ReloadAsync();
        await Expect(Window.Summary).ToHaveValueAsync("Measure a thing and it bends.");
    }

    [Test, Order(5)]
    public async Task NewEntry_WhenLeftEmpty_ShouldBeDiscarded()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        var posts = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith("/kb/entries", StringComparison.Ordinal))
            {
                posts.Add(request.Url);
            }
        };
        await Expect(Sidebar.Item(SeedIds.KbEntries.KentBeck)).ToBeVisibleAsync();
        var before = await Sidebar.Category(SeedIds.KbCategories.People).TextContentAsync();

        await Window.NewEntryButton(SeedIds.KbCategories.People).ClickAsync();
        await Expect(Window.DraftName).ToBeFocusedAsync();
        await Window.Close.ClickAsync();

        await Expect(Window.Root).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await Expect(Sidebar.Category(SeedIds.KbCategories.People)).ToHaveTextAsync(before!);
        posts.ShouldBeEmpty();
    }

    [Test, Order(6)]
    public async Task Reorder_WhenAltArrowDown_ShouldMoveTheEntryAfterItsNeighbour()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToBeVisibleAsync();
        var before = await EntryOrderAsync(SeedIds.KbCategories.Laws);
        var first = Guid.Parse(before[0]);
        await Page.GotoAsync(EntryUrl(first));
        await Expect(Window.Position).ToHaveTextAsync(new Regex(@"^1 of "));

        var moved = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{first}/position", "PUT", () => Page.Keyboard.PressAsync("Alt+ArrowDown"));

        moved.Status.ShouldBe(204);
        await Expect(Window.Position).ToHaveTextAsync(new Regex(@"^2 of "));
        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws).GetByTestId(new Regex("^shell-kb-item-")).First)
            .ToHaveAttributeAsync("data-testid", $"shell-kb-item-{before[1]}");
        (await EntryOrderAsync(SeedIds.KbCategories.Laws)).ShouldBe([before[1], before[0], .. before.Skip(2)]);
    }

    private async Task<string> EntryIdFromUrlAsync()
    {
        var match = Regex.Match(Page.Url, "entry=([0-9a-f-]{36})");
        match.Success.ShouldBeTrue();
        return await Task.FromResult(match.Groups[1].Value);
    }

    private async Task<List<string>> EntryOrderAsync(Guid categoryId)
    {
        var ids = await Sidebar.Category(categoryId).GetByTestId(new Regex("^shell-kb-item-"))
            .EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-testid').replace('shell-kb-item-', ''))");
        return [.. ids];
    }
}
