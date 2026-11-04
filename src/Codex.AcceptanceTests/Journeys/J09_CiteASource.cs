using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Codex.Contracts.Sources;

namespace Codex.AcceptanceTests.Journeys;

// J9 · Cite a source while writing (#16 Part 2). Goal A: cite Accelerate again in Ch 7 · Sc 1 (marker 8, warn).
// Goal B: cite a book that is not in References yet through New source… and the ISBN lookup, return to Write with the
// marker in place and the numbers after it shifted, then meet the same ISBN again and be offered the record.
[TestFixture, Category("J9")]
public class J09_CiteASource : JourneyTest
{
    private const string Isbn = "978-1-7364260-0-5";
    private const string Title = "Shape Up";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CitePassageResult _accelerate = null!;
    private static CitePassageResult _shapeUp = null!;
    private static string _sentenceA = "";
    private static string _sentenceB = "";
    private static string _addSourceUrl = "";

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private static string WriteUrl => Links.Write(BookId, Links.Hash(("scene", "7.1")));

    // The first sentence of the n-th paragraph (the scene's filler paragraphs hold 12-word sentences).
    private async Task<string> SentenceAsync(int paragraph)
    {
        var body = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene));
        var text = body!.Split("\n\n")[paragraph];
        return text[..(text.IndexOf('.') + 1)];
    }

    private async Task<WritePageObject> OpenWriteAsync()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(WriteUrl);
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        return write;
    }

    [Test, Order(1)]
    public async Task Step1_WhenSentenceSelectedInCh7Sc1_ShouldHoldTheSelection()
    {
        await OpenWriteAsync();
        _sentenceA = await SentenceAsync(1);

        // Deviation: the seeded Ch 7 · Sc 1 is deterministic filler, so no sentence "paraphrases Accelerate"; the
        // first sentence of its second paragraph stands in.
        await new CitePage(Page).SelectAsync(Scene, _sentenceA);

        (await Page.EvaluateAsync<string>("() => window.getSelection().toString()")).ShouldBe(_sentenceA);
    }

    [Test, Order(2)]
    public async Task Step2_WhenAccelerateChosenFromSourceReference_ShouldWrapPassageAsMarker8()
    {
        var write = await OpenWriteAsync();
        await Expect(write.Markers).ToHaveCountAsync(15);
        var cite = new CitePage(Page);
        await cite.SelectAsync(Scene, _sentenceA);
        await Page.Keyboard.TypeAsync("/source");
        await cite.SourceReference.ClickAsync();
        await Expect(cite.Picker).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => cite.Option(SeedIds.References.Accelerate).ClickAsync());

        response.Ok.ShouldBeTrue();
        _accelerate = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        _accelerate.Body.ShouldContain($"[{_sentenceA}](ref:{_accelerate.UseId})");
        await Expect(write.Marker(_accelerate.UseId)).ToHaveAttributeAsync("data-number", "8");
        await Expect(write.Markers).ToHaveCountAsync(16);
        (await ScalarSqlAsync<string>("SELECT kind FROM reference_use WHERE id = @id", ("id", _accelerate.UseId))).ShouldBe("paraphrase");
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene))).ShouldBe(_accelerate.Body);
    }

    [Test, Order(3)]
    public async Task Step3_WhenMarker8Examined_ShouldBeWarnBecauseAccelerateHasTwoUsesSinceSignOff()
    {
        var write = await OpenWriteAsync();
        await Expect(write.Marker(_accelerate.UseId)).ToHaveAttributeAsync("data-tone", "warn");

        await write.Marker(_accelerate.UseId).ClickAsync();

        await Expect(write.MarkerCardSource).ToContainTextAsync("Accelerate");
        await Expect(write.MarkerCardStatus).ToContainTextAsync("Needs check");
        await Expect(write.MarkerCardReason).ToContainTextAsync("New uses since signoff");

        // Deviation: the journey says 2 uses since sign-off; as seeded Accelerate already has two (Ch 5 · Sc 2 and
        // Ch 7 · Sc 1, both after the sign-off), so with this citation it has 3. The marker card shows the reason only.
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_use u JOIN reference r ON r.id = u.reference_id WHERE r.id = @id AND u.created_at > r.signed_off_at",
            ("id", SeedIds.References.Accelerate))).ShouldBe(3);
    }

    [Test, Order(4)]
    public async Task Step4_WhenNewSourceChosenFromSourceReference_ShouldOpenAddSourceWithReturnNote()
    {
        await OpenWriteAsync();
        _sentenceB = await SentenceAsync(3);
        var cite = new CitePage(Page);
        await cite.SelectAsync(Scene, _sentenceB);
        await Page.Keyboard.TypeAsync("/source");
        await cite.SourceReference.ClickAsync();

        await cite.NewSource.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/references\?.*#new$"));
        await Expect(new CiteHandOffPage(Page).ReturnNote).ToHaveTextAsync("Will return to Write · Chapter 7 · Scene 1");
        await Expect(new AddSourceForm(Page).Form).ToBeVisibleAsync();
        _addSourceUrl = Page.Url;
    }

    [Test, Order(5)]
    public async Task Step5_WhenIsbnLookedUpAndSaved_ShouldReturnToWriteAndCiteTheSelection()
    {
        await Page.GotoAsync(_addSourceUrl);
        var add = new AddSourceForm(Page);
        await Expect(add.Form).ToBeVisibleAsync();
        await add.Input.FillAsync(Isbn);

        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => add.Lookup.ClickAsync());

        response.Ok.ShouldBeTrue();
        _shapeUp = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        _shapeUp.Body.ShouldContain($"[{_sentenceB}](ref:{_shapeUp.UseId})");
        await Expect(Page).ToHaveURLAsync(BaseUrl + WriteUrl);
        var write = new WritePageObject(Page);
        await Expect(write.Marker(_shapeUp.UseId)).ToHaveAttributeAsync("data-number", _shapeUp.Number.ToString(CultureInfo.InvariantCulture));
        await Expect(new CiteHandOffPage(Page).Toasts.Filter(new() { HasText = $"Cited {Title}" })).ToBeVisibleAsync();
        (await ScalarSqlAsync<string>("SELECT r.title FROM reference r JOIN reference_use u ON u.reference_id = r.id WHERE u.id = @id", ("id", _shapeUp.UseId))).ShouldBe(Title);
    }

    [Test, Order(6)]
    public async Task Step6_WhenNewSourceCited_ShouldShiftLaterMarkersByOneAndAgreeWithReferences()
    {
        // The three later sources (Team Topologies, Mythical Man-Month, Ship It Friday) each move up by one.
        var write = await OpenWriteAsync();
        var shapeUpNumber = _shapeUp.Number;
        shapeUpNumber.ShouldBe(9);
        _shapeUp.MarkersShifted.ShouldBe(3);
        await Expect(write.Marker(SeedIds.ReferenceUses.TeamTopologiesCh9Sc1)).ToHaveAttributeAsync("data-number", "10");
        var sourceId = await ScalarSqlAsync<Guid>("SELECT reference_id FROM reference_use WHERE id = @id", ("id", _shapeUp.UseId));

        await new BookBar(Page).TabReferences.ClickAsync();

        var references = new ReferencesScreen(Page);
        await Expect(references.Row(sourceId).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync(shapeUpNumber.ToString(CultureInfo.InvariantCulture));
        await Expect(references.Row(SeedIds.References.TeamTopologies).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("10");
        await Expect(references.Row(SeedIds.References.Accelerate).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("8");
        await Expect(references.Row(SeedIds.References.MythicalManMonth).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("11");
        await Expect(references.Row(SeedIds.References.ShipItFriday).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("12");
    }

    [Test, Order(7)]
    public async Task Step7_WhenSameIsbnPastedAgain_ShouldOfferTheExistingRecordInsteadOfADuplicate()
    {
        await OpenWriteAsync();
        var sentence = await SentenceAsync(4);
        var cite = new CitePage(Page);
        await cite.SelectAsync(Scene, sentence);
        await Page.Keyboard.TypeAsync("/source");
        await cite.SourceReference.ClickAsync();
        await cite.NewSource.ClickAsync();
        var add = new AddSourceForm(Page);
        await add.Input.FillAsync(Isbn);

        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references/lookup", "POST", () => add.Lookup.ClickAsync());

        await Expect(add.MatchTitle).ToHaveTextAsync(Title);
        await Expect(add.UseExisting).ToContainTextAsync($"Use existing source: {Title}");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @book AND title = @title", ("book", SeedIds.Books.ProductEngineering), ("title", Title))).ShouldBe(1);

        // From Write, Use existing source cites it and returns at once (there is no separate return bar).
        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => add.UseExisting.ClickAsync());

        response.Ok.ShouldBeTrue();
        var second = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        await Expect(Page).ToHaveURLAsync(BaseUrl + WriteUrl);
        var write = new WritePageObject(Page);
        await Expect(write.Marker(second.UseId)).ToHaveAttributeAsync("data-number", "9");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @book AND title = @title", ("book", SeedIds.Books.ProductEngineering), ("title", Title))).ShouldBe(1);
    }
}
