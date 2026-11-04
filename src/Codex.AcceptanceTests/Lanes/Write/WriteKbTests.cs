using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// The Knowledge base in the manuscript, from the seed: name underlines, the highlight chip stepping through an entry's
// uses, a Codex addition inserted and edited, and Ctrl+click on a mention.
[TestFixture]
public class WriteKbTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Goodhart = SeedIds.KbEntries.GoodhartsLaw;
    private static readonly Guid TypedIn = SeedIds.Scenes.Ch5Sc1;
    private static readonly Guid AdditionScene = SeedIds.Scenes.Ch4Sc1;

    private static async Task<ProseDto> GetProseAsync(string baseUrl, Guid sceneId)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;
    }

    [Test, Order(1)]
    public async Task Alias_ShouldBeUnderlined()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(TypedIn)).ToBeVisibleAsync();
        await Expect(write.Aliases(TypedIn)).ToHaveCountAsync(0);

        // "Goodhart" is an alias of Goodhart's Law; typed as plain text it gets the dotted underline.
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{TypedIn}/prose", "PUT", async () =>
        {
            await write.PlaceCaretAtStartAsync(TypedIn);
            await Page.Keyboard.TypeAsync("Goodhart noticed the drift. ");
            await write.Manuscript.FocusAsync();
        });

        response.Ok.ShouldBeTrue();
        var alias = write.Aliases(TypedIn);
        await Expect(alias).ToHaveCountAsync(1);
        await Expect(alias).ToHaveTextAsync("Goodhart");
        await Expect(alias).ToHaveAttributeAsync("data-kb-entry", Goodhart.ToString());
        await Expect(alias).ToHaveCSSAsync("border-bottom-style", "dotted");
    }

    [Test, Order(2)]
    public async Task Highlight_ShouldStepThroughUsesAndClear()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("kb", Goodhart.ToString()))));

        // The Ch 4 Sc 1 addition card comes first in the book, the name typed in Ch 5 Sc 1 second.
        await Expect(write.HighlightName).ToHaveTextAsync("Goodhart's Law");
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");
        await write.HighlightNext.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");
        await write.HighlightNext.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("2 of 2");
        await write.HighlightPrevious.ClickAsync();
        await Expect(write.HighlightPosition).ToHaveTextAsync("1 of 2");
        await Expect(write.MinimapTick(AdditionScene)).ToHaveAttributeAsync("data-used", "true");
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch3Sc1)).ToHaveAttributeAsync("data-used", "false");

        await write.HighlightClear.ClickAsync();

        await Expect(write.HighlightChip).ToHaveCountAsync(0);
        await Expect(Page).Not.ToHaveURLAsync(new Regex("kb="));
        (await Page.EvaluateAsync<string?>("id => sessionStorage.getItem('codex.hl.' + id)", BookId)).ShouldBeNull();
        await Expect(write.MinimapTick(AdditionScene)).ToHaveAttributeAsync("data-used", "false");
    }

    [Test, Order(3)]
    public async Task Addition_WhenSaved_ShouldBeInProse()
    {
        var write = new WritePageObject(Page);
        var scene = SeedIds.Scenes.Ch5Sc1;
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(scene)).ToBeVisibleAsync();
        var cards = await write.AdditionCards(scene).CountAsync();

        var inserted = await Page.RunAndWaitForApiAsync($"/api/scenes/{scene}/kb-additions", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(scene);
            await Page.Keyboard.TypeAsync("/codex");
            await write.SlashItem(WriteTestIds.SlashCodexAddition).ClickAsync();
            await write.EntryPickerOption(SeedIds.KbEntries.ConwaysLaw).ClickAsync();
        });

        var result = JsonSerializer.Deserialize<InsertKbAdditionResult>(await inserted.TextAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Expect(write.AdditionCards(scene)).ToHaveCountAsync(cards + 1);
        await Expect(write.AdditionEntry(result.ProgressionId)).ToContainTextAsync("Conway's Law");
        var patched = await Page.RunAndWaitForApiAsync($"/api/kb-additions/{result.ProgressionId}", "PATCH", async () =>
        {
            await write.AdditionBody(result.ProgressionId).FillAsync("From here the reader knows the inverse Conway manoeuvre.");
            await write.Manuscript.FocusAsync();
        });
        patched.Ok.ShouldBeTrue();
        (await GetProseAsync(BaseUrl, scene)).Body.ShouldContain($"::kb-addition{{id=\"{result.ProgressionId}\"}}");
        (await ScalarSqlAsync<string>("SELECT body FROM kb_progression WHERE id = @id", ("id", result.ProgressionId)))
            .ShouldBe("From here the reader knows the inverse Conway manoeuvre.");
    }

    [Test, Order(4)]
    public async Task Mention_WhenCtrlClicked_ShouldOpenEntryHash()
    {
        var write = new WritePageObject(Page);
        var scene = SeedIds.Scenes.Ch3Sc1;
        await Page.GotoAsync(Links.Write(BookId));
        var mention = write.Mentions(scene).First;
        await Expect(mention).ToBeVisibleAsync();
        await Expect(mention).ToHaveAttributeAsync("data-kb-entry", SeedIds.KbEntries.KentBeck.ToString());

        await mention.ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Control] });

        await Expect(Page).ToHaveURLAsync(new Regex($"#entry={SeedIds.KbEntries.KentBeck}"));
    }

    [Test, Order(5)]
    public async Task Addition_WhenSceneVersionIsStale_ShouldBeRefusedAndLeaveNoRow()
    {
        // A stale scene version is refused and leaves no row behind.
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var current = await GetProseAsync(BaseUrl, SeedIds.Scenes.Ch3Sc1);
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/kb-additions",
            new InsertKbAdditionRequest(id, SeedIds.KbEntries.ConwaysLaw, "x", current.Body + $"\n::kb-addition{{id=\"{id}\"}}\n", current.Version + 5));

        ((int)response.StatusCode).ShouldBe(409);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_progression WHERE id = @id", ("id", id))).ShouldBe(0);
    }
}
