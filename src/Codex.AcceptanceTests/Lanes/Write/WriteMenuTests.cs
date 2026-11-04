using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// The `/` menu items that change structure: a chapter reference and a new scene from the caret.
[TestFixture]
public class WriteMenuTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;

    private async Task<ProseDto> GetProseAsync(Guid sceneId)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;
    }

    [Test, Order(1)]
    public async Task ChapterReference_ShouldInsertSectionMark()
    {
        var write = new WritePageObject(Page);
        var chapter = SeedIds.Chapters.Ch3;
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        var before = await write.ChapterRefsIn(Scene).CountAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose", "PUT", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("See /chapter");
            await write.SlashItem(WriteTestIds.SlashChapterReference).ClickAsync();
            await write.ChapterPickerOption(chapter).ClickAsync();
            await write.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.ChapterRefsIn(Scene)).ToHaveCountAsync(before + 1);
        await Expect(write.ChapterRefsIn(Scene).First).ToContainTextAsync("§ Chapter 3");
        // Clicking the picker blurs the editor, so the PUT above can be an earlier save; the one with the link may wait
        // for it and then for the 3 s autosave. Every edit is on the server once the bar reads Saved.
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 10_000 });
        (await GetProseAsync(Scene)).Body.ShouldContain($"(chapter:{chapter})");
    }

    [Test, Order(2)]
    public async Task NewSceneFromHere_ShouldSplitScene()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        var scenes = await write.Scenes.CountAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/split", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("/split");
            await write.SlashItem(WriteTestIds.SlashNewSceneFromHere).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        var result = JsonSerializer.Deserialize<SplitSceneResult>(await response.TextAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Expect(write.Scenes).ToHaveCountAsync(scenes + 1);
        var head = await GetProseAsync(Scene);
        var tail = await GetProseAsync(result.NewSceneId);
        head.Version.ShouldBe(result.Version);
        head.Body.ShouldNotContain("::beat{");
        tail.Body.ShouldContain($"::beat{{id=\"{SeedIds.Beats.Ch5Sc1EngineersInDiscovery}\"}}");
        tail.Body.ShouldContain($"::beat{{id=\"{SeedIds.Beats.Ch5Sc1WhatChanged}\"}}");
        await Expect(write.Scene(result.NewSceneId)).ToBeVisibleAsync();
    }
}
