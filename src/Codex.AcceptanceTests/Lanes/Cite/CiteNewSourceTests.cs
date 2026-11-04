using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts.Citations;

namespace Codex.AcceptanceTests.Lanes.Cite;

// J9 B: `/` → Source reference → New source… in Ch 7 Sc 1 opens References' Add source to return to the scene.
// Saving a source entered by hand comes back to Write and cites the selected sentence for it; Cancel comes back with
// nothing cited and the text as it was.
[TestFixture]
public class CiteNewSourceTests : CiteLaneTest
{
    private const string Title = "Shape Up";

    private async Task<string> OpenAddSourceAsync(int paragraph)
    {
        await OpenAsync();
        var sentence = Sentence((await GetProseAsync()).Body, paragraph);
        var cite = new CitePage(Page);
        await cite.SelectAsync(Scene, sentence);
        await Page.Keyboard.TypeAsync("/source");
        await cite.SourceReference.ClickAsync();
        await cite.NewSource.ClickAsync();
        await Expect(new CiteHandOffPage(Page).ReturnNote).ToHaveTextAsync("Will return to Write · Chapter 7 · Scene 1");
        return sentence;
    }

    [Test, Order(1)]
    public async Task Save_WhenANewSourceIsAddedFromWrite_ShouldReturnAndInsertTheMarker()
    {
        var sentence = await OpenAddSourceAsync(3);
        var add = new AddSourceForm(Page);
        await add.Manual.ClickAsync();
        await add.Field("title").FillAsync(Title);

        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => add.Save.ClickAsync());

        response.Ok.ShouldBeTrue();
        var result = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        result.Body.ShouldContain($"[{sentence}](ref:{result.UseId})");
        await Expect(Page).ToHaveURLAsync(BaseUrl + Links.Write(BookId, Links.Hash(("scene", "7.1"))));
        var write = new Pages.Write.WritePageObject(Page);
        await Expect(write.Marker(result.UseId)).ToHaveAttributeAsync("data-tone", "warn");
        await Expect(write.Marker(result.UseId)).ToHaveAttributeAsync("data-number", result.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Expect(new CiteHandOffPage(Page).Toasts.Filter(new() { HasText = $"Cited {Title}" })).ToBeVisibleAsync();
        (await GetProseAsync()).Body.ShouldBe(result.Body);
    }

    [Test, Order(2)]
    public async Task Cancel_WhenAddSourceIsLeft_ShouldReturnToTheSceneWithNothingCited()
    {
        var before = (await GetProseAsync()).Body;
        await OpenAddSourceAsync(4);

        await new AddSourceForm(Page).Form.GetByTestId(ReferencesTestIds.AddCancel).ClickAsync();

        await Expect(Page).ToHaveURLAsync(BaseUrl + Links.Write(BookId, Links.Hash(("scene", "7.1"))));
        var write = new Pages.Write.WritePageObject(Page);
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(write.Markers).ToHaveCountAsync(16);
        (await GetProseAsync()).Body.ShouldBe(before);
    }
}
