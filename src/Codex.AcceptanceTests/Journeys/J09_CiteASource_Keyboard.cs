using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J9 · Cite a source while writing (#16 Part 2), keyboard only (#16 review section D): the same two goals as
// J09_CiteASource (cite Accelerate again in Ch 7 · Sc 1; cite a book through New source… and the ISBN lookup), with
// Tab, Shift+Arrow selection, Enter and Escape and no pointer after the start screen.
[TestFixture, Category("J9")]
public class J09_CiteASource_Keyboard : JourneyTest
{
    private const string Isbn = "978-1-7364260-0-5";
    private const string Title = "Shape Up";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CitePassageResult _accelerate = null!;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private static string WriteUrl => Links.Write(BookId, Links.Hash(("scene", "7.1")));

    // The first sentence of the n-th paragraph (the scene's filler paragraphs hold 12-word sentences).
    private async Task<string> SentenceAsync(int paragraph)
    {
        var body = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene));
        var text = body!.Split("\n\n")[paragraph];
        return text[..(text.IndexOf('.') + 1)];
    }

    // Tabs into the scene's editor, walks the caret down to the start of the n-th paragraph and selects the first
    // sentence with Shift+ArrowRight, all from the keyboard.
    private async Task SelectSentenceAsync(int paragraph, string sentence)
    {
        var editor = new WritePageObject(Page).ProseEditor(Scene);
        await Page.TabToAsync(editor);
        await Page.Keyboard.PressAsync("Control+Home");
        for (var presses = 0; presses < 60 && await ParagraphIndexAsync(editor) < paragraph; presses++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
        }

        await Page.Keyboard.PressAsync("Home");
        for (var i = 0; i < sentence.Length; i++)
        {
            await Page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        (await Page.EvaluateAsync<string>("() => window.getSelection().toString()")).ShouldBe(sentence);
    }

    private static Task<int> ParagraphIndexAsync(ILocator editor) =>
        editor.EvaluateAsync<int>(
            """
            element => {
                const node = window.getSelection().anchorNode;
                const block = (node instanceof Element ? node : node.parentElement).closest('p');
                return block ? [...element.querySelectorAll(':scope > p')].indexOf(block) : 0;
            }
            """);

    private async Task<WritePageObject> OpenWriteAsync()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(WriteUrl);
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(write.Markers.First).ToBeVisibleAsync();
        return write;
    }

    [Test, Order(1)]
    public async Task Step1_WhenAccelerateChosenFromSourceReferenceByKeyboard_ShouldWrapPassageAsMarker8()
    {
        var write = await OpenWriteAsync();
        await Expect(write.Markers).ToHaveCountAsync(15);
        var cite = new CitePage(Page);
        var sentence = await SentenceAsync(1);
        await SelectSentenceAsync(1, sentence);
        await Page.Keyboard.TypeAsync("/source");
        await Expect(cite.SourceReference).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(cite.Picker).ToBeVisibleAsync();
        await Expect(cite.Search).ToBeFocusedAsync();

        // Tab from the search field to the option and Space it: the list is a keyboard path of its own.
        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => Page.ActivateAsync(cite.Option(SeedIds.References.Accelerate), "Space"));

        response.Ok.ShouldBeTrue();
        _accelerate = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        _accelerate.Body.ShouldContain($"[{sentence}](ref:{_accelerate.UseId})");
        await Expect(write.Marker(_accelerate.UseId)).ToHaveAttributeAsync("data-number", "8");
        await Expect(cite.Picker).ToHaveCountAsync(0);
        (await ScalarSqlAsync<string>("SELECT kind FROM reference_use WHERE id = @id", ("id", _accelerate.UseId))).ShouldBe("paraphrase");
    }

    [Test, Order(2)]
    public async Task Step2_WhenMarker8OpenedAndClosedByKeyboard_ShouldShowWarnCardAndKeepFocusOnTheMarker()
    {
        var write = await OpenWriteAsync();
        await Expect(write.Marker(_accelerate.UseId)).ToHaveAttributeAsync("data-tone", "warn");

        await Page.ActivateAsync(write.Marker(_accelerate.UseId));

        await Expect(write.MarkerCardSource).ToContainTextAsync("Accelerate");
        await Expect(write.MarkerCardStatus).ToContainTextAsync("Needs check");
        await Expect(write.MarkerCardReason).ToContainTextAsync("New uses since signoff");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(write.MarkerCard).ToHaveCountAsync(0);
        await Expect(write.Marker(_accelerate.UseId)).ToBeFocusedAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenNewSourceChosenByKeyboardAndIsbnLookedUp_ShouldReturnToWriteAndCiteTheSelection()
    {
        await OpenWriteAsync();
        var cite = new CitePage(Page);
        var sentence = await SentenceAsync(3);
        await SelectSentenceAsync(3, sentence);
        await Page.Keyboard.TypeAsync("/source");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(cite.Picker).ToBeVisibleAsync();

        await Page.ActivateAsync(cite.NewSource);

        await Expect(Page).ToHaveURLAsync(new Regex(@"/references\?.*#new$"));
        await Expect(new CiteHandOffPage(Page).ReturnNote).ToHaveTextAsync("Will return to Write · Chapter 7 · Scene 1");
        var add = new AddSourceForm(Page);
        await Expect(add.Form).ToBeVisibleAsync();
        await Page.TabToAsync(add.Input);
        await Page.Keyboard.TypeAsync(Isbn);

        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => Page.ActivateAsync(add.Lookup));

        response.Ok.ShouldBeTrue();
        var cited = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        cited.Body.ShouldContain($"[{sentence}](ref:{cited.UseId})");
        await Expect(Page).ToHaveURLAsync(BaseUrl + WriteUrl);
        await Expect(new WritePageObject(Page).Marker(cited.UseId)).ToHaveAttributeAsync("data-number", cited.Number.ToString(CultureInfo.InvariantCulture));
        await Expect(new CiteHandOffPage(Page).Toasts.Filter(new() { HasText = $"Cited {Title}" })).ToBeVisibleAsync();
    }

    [Test, Order(4)]
    public async Task Step4_WhenSameIsbnTypedAgain_ShouldOfferTheExistingRecordByKeyboard()
    {
        await OpenWriteAsync();
        var cite = new CitePage(Page);
        var sentence = await SentenceAsync(4);
        await SelectSentenceAsync(4, sentence);
        await Page.Keyboard.TypeAsync("/source");
        await Page.Keyboard.PressAsync("Enter");
        await Page.ActivateAsync(cite.NewSource);
        var add = new AddSourceForm(Page);
        await Page.TabToAsync(add.Input);
        await Page.Keyboard.TypeAsync(Isbn);
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references/lookup", "POST", () => Page.ActivateAsync(add.Lookup));
        await Expect(add.UseExisting).ToContainTextAsync($"Use existing source: {Title}");

        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => Page.ActivateAsync(add.UseExisting));

        response.Ok.ShouldBeTrue();
        await Expect(Page).ToHaveURLAsync(BaseUrl + WriteUrl);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @book AND title = @title", ("book", SeedIds.Books.ProductEngineering), ("title", Title))).ShouldBe(1);
    }
}
