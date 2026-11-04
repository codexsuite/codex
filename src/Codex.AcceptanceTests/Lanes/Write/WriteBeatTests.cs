using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// Beats: inserting one from the `/` menu, its length and context, and dragging one (with its prose) to another scene.
[TestFixture]
public class WriteBeatTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;
    private static readonly Guid OtherScene = SeedIds.Scenes.Ch5Sc2;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc1WhatChanged;

    private async Task<ProseDto> GetProseAsync(Guid sceneId)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;
    }

    private async Task<BeatDto> GetBeatAsync(Guid beatId)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var beats = (await client.GetFromJsonAsync<BookBeatsDto>($"/api/books/{BookId}/beats"))!;
        return beats.Beats.Single(b => b.Id == beatId);
    }

    [Test, Order(1)]
    public async Task Beat_WhenInserted_ShouldSplitParagraph()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(2);
        var before = await GetProseAsync(Scene);

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/beats", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("Marty /scene");
            await write.SlashItem(WriteTestIds.SlashSceneBeat).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(3);
        var after = await GetProseAsync(Scene);
        after.Version.ShouldBeGreaterThan(before.Version);
        var blocks = after.Body.Split("\n\n");
        blocks[0].ShouldBe("Marty");
        blocks[1].ShouldStartWith("::beat{id=\"");
        blocks[2].ShouldContain("argues that engineers");
        after.Body.Split("::beat{").Length.ShouldBe(4);
    }

    [Test, Order(2)]
    public async Task Length_When600Chosen_ShouldSave()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatLength(Beat, 600)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}", "PATCH", async () =>
            await write.BeatLength(Beat, 600).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.BeatLength(Beat, 600)).ToHaveAttributeAsync("aria-pressed", "true");
        (await GetBeatAsync(Beat)).TargetWordCount.ShouldBe(600);
    }

    [Test, Order(3)]
    public async Task Context_WhenAdded_ShouldSave()
    {
        var write = new WritePageObject(Page);
        var entry = SeedIds.KbEntries.GoodhartsLaw;
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatContextAdd(Beat)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/context", "PUT", async () =>
        {
            await write.BeatContextAdd(Beat).ClickAsync();
            await write.BeatContextSearch.FillAsync("Goodhart");
            await write.BeatContextOption(entry).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.NoContent);
        await Expect(write.BeatContextChip(Beat, entry)).ToBeVisibleAsync();
        (await GetBeatAsync(Beat)).ContextEntryIds.ShouldContain(entry);
    }

    [Test, Order(4)]
    public async Task Beat_WhenAltDownAtSceneEnd_ShouldMoveToNextScene()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatGrip(Beat)).ToBeVisibleAsync();
        var source = await GetProseAsync(Scene);
        var target = await GetProseAsync(OtherScene);

        var response = await Page.RunAndWaitForApiAsync("/api/blocks/move", "POST", async () =>
        {
            await write.BeatGrip(Beat).FocusAsync();
            await Page.Keyboard.PressAsync("Alt+ArrowDown");
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await AssertMovedAsync(write, from: Scene, to: OtherScene, source, target);
    }

    // Runs after the Alt+Down step, so the beat starts in the second scene and is dragged back to the first.
    [Test, Order(5)]
    public async Task Beat_WhenDraggedToAnotherScene_ShouldTakeItsProse()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        // Tall enough that the grip and the scene above are both on screen: a drag cannot scroll the page.
        await Page.SetViewportSizeAsync(1280, 2600);
        await Expect(write.BeatGrip(Beat)).ToBeVisibleAsync();
        await write.BeatGrip(Beat).ScrollIntoViewIfNeededAsync();
        var source = await GetProseAsync(OtherScene);
        var target = await GetProseAsync(Scene);

        var response = await Page.RunAndWaitForApiAsync("/api/blocks/move", "POST", async () =>
        {
            var grip = (await write.BeatGrip(Beat).BoundingBoxAsync())!;
            var drop = (await write.Scene(Scene).BoundingBoxAsync())!;
            var (x, y) = (grip.X + grip.Width / 2, grip.Y + grip.Height / 2);
            await Page.Mouse.MoveAsync(x, y);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(x + 10, y - 10, new MouseMoveOptions { Steps = 5 });
            await Page.Mouse.MoveAsync(drop.X + drop.Width / 2, drop.Y + drop.Height - 20, new MouseMoveOptions { Steps = 20 });
            await Page.Mouse.UpAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await AssertMovedAsync(write, from: OtherScene, to: Scene, source, target);
    }

    private async Task AssertMovedAsync(WritePageObject write, Guid from, Guid to, ProseDto source, ProseDto target)
    {
        var movedFrom = await GetProseAsync(from);
        var movedTo = await GetProseAsync(to);
        movedFrom.Body.ShouldNotContain(Beat.ToString());
        movedTo.Body.ShouldContain($"::beat{{id=\"{Beat}\"}}");
        // The beat took the paragraph under it: the target holds one more block than the beat alone.
        movedTo.Body.Split("\n\n").Length.ShouldBeGreaterThan(target.Body.Split("\n\n").Length + 1);
        movedFrom.Body.Length.ShouldBeLessThan(source.Body.Length);
        (await GetBeatAsync(Beat)).SceneId.ShouldBe(to);
        await Expect(write.Scene(to).GetByTestId(WriteTestIds.BeatGrip(Beat))).ToBeVisibleAsync();
    }
}
