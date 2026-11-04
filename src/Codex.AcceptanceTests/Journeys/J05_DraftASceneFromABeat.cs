using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J5 · Draft a scene from a beat (#16 Part 2): the empty Ch 3 · Sc 3 (Bad Scrum) gets a first draft: beat length and
// context, template and model, stream, Retry then Apply attempt 2, an edit by hand, Format -> Hide beats.
// The AI provider is a fake that streams two sentences and holds after the first until the journey releases it.
[TestFixture, Category("J5")]
public class J05_DraftASceneFromABeat : JourneyTest
{
    private const string FirstSentence = "Scrum arrived as a set of habits and left as a methodology. ";
    private const string SecondSentence = "By then nobody remembered what the habits were for.";
    private const string HandEdit = " I watched it happen at three companies.";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch3Sc3;
    private static readonly Guid Beat = SeedIds.Beats.Ch3Sc3BadScrum;
    private static readonly TaskCompletionSource Release = new();
    private static readonly FakeProseGenerator Generator = new([FirstSentence, SecondSentence], Release.Task);

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    [Test, Order(1)]
    public async Task Step1_WhenChapter3ChosenInJumpMenu_ShouldScrollToScene3WithHollowTick()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Scene(Scene)).ToBeVisibleAsync();

        await write.ChapterJump.SelectOptionAsync(SeedIds.Chapters.Ch3.ToString());
        await write.Scene(Scene).ScrollIntoViewIfNeededAsync();

        await Expect(write.Scene(Scene)).ToBeInViewportAsync();
        await Expect(write.MinimapTick(Scene)).ToHaveAttributeAsync("data-kind", "empty");
    }

    [Test, Order(2)]
    public async Task Step2_OnBadScrumBeat_ShouldMentionSignsOfBadScrumAndHold600Words()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatBody(Beat)).ToHaveTextAsync("Bad Scrum.");

        // Deviation from the journey text: the instruction that mentions @Signs of Bad Scrum is the scene's summary
        // (the gutter), the beat itself reads "Bad Scrum.". The seed also stores the length 600 already, so choosing
        // it is a no-op the journey still performs.
        await Expect(write.SceneSummary(Scene)).ToContainTextAsync("Signs of Bad Scrum");
        await write.BeatLength(Beat, 600).ClickAsync();

        await Expect(write.BeatLength(Beat, 600)).ToHaveAttributeAsync("aria-pressed", "true");
        (await ScalarSqlAsync<int>("SELECT target_word_count FROM beat WHERE id = @id", ("id", Beat))).ShouldBe(600);
    }

    [Test, Order(3)]
    public async Task Step3_WhenAgileManifestoAddedAsContext_ShouldShowChipAndSaveBeatKb()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatContextAdd(Beat)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/context", "PUT", async () =>
        {
            await write.BeatContextAdd(Beat).ClickAsync();
            await write.BeatContextSearch.FillAsync("Agile");
            await write.BeatContextOption(SeedIds.KbEntries.AgileManifesto).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.NoContent);
        await Expect(write.BeatContextChip(Beat, SeedIds.KbEntries.AgileManifesto)).ToContainTextAsync("Agile Manifesto (2001)");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM beat_kb WHERE beat_id = @beat AND kb_entry_id = @entry AND archived_at IS NULL",
            ("beat", Beat), ("entry", SeedIds.KbEntries.AgileManifesto))).ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task Step4_WhenJoelsVoiceAndSonnetChosen_GenerateShouldStreamWithCaretAndStop()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatChoice(Beat)).ToBeVisibleAsync();
        await write.BeatChoice(Beat).ClickAsync();
        await write.DraftTemplate("joels-voice").ClickAsync();
        await write.DraftModel(AiIntegrationRows.IntegrationId, "claude-sonnet-5-5").ClickAsync();
        await Expect(write.BeatChoice(Beat)).ToHaveTextAsync("Write in Joel's voice · Sonnet 5.5");

        var response = await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", () => write.BeatGenerate(Beat).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "streaming");
        await Expect(write.DraftText(Beat)).ToContainTextAsync("Scrum arrived as a set of habits");
        await Expect(write.DraftCaret(Beat)).ToBeVisibleAsync();
        await Expect(write.DraftStop(Beat)).ToBeVisibleAsync();
        (await ScalarSqlAsync<string>("SELECT model FROM ai_generation WHERE beat_id = @beat", ("beat", Beat))).ShouldBe("claude-sonnet-5-5");
        (await ScalarSqlAsync<string>("SELECT request->>'templateId' FROM ai_generation WHERE beat_id = @beat", ("beat", Beat))).ShouldBe("joels-voice");
        Release.SetResult();
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftCaret(Beat)).ToHaveCountAsync(0);
        (await ScalarSqlAsync<int>("SELECT (request->>'targetWords')::int FROM ai_generation WHERE beat_id = @beat", ("beat", Beat))).ShouldBe(600);
        (await ScalarSqlAsync<string>("SELECT request->'context'->>0 FROM ai_generation WHERE beat_id = @beat", ("beat", Beat))).ShouldBe("Agile Manifesto (2001)");
    }

    [Test, Order(5)]
    public async Task Step5_WhenRetriedOnceThenAttemptTwoApplied_ShouldWriteItWithRetryLink()
    {
        var write = new WritePageObject(Page);
        // Each step has its own browser page and closing one stops its stream, so step 4 saw the stream (caret, Stop)
        // and let it finish; the finished draft is what step 5 finds.
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToContainTextAsync(SecondSentence);

        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", () => write.DraftRetry(Beat).ClickAsync());
        await Expect(write.DraftMeta(Beat)).ToContainTextAsync("attempt 2");
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        var second = await write.Draft(Beat).GetAttributeAsync("data-generation-id");
        var applied = await Page.RunAndWaitForApiAsync($"/api/generations/{second}/apply", "POST", () => write.DraftApply(Beat).ClickAsync());

        applied.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync(FirstSentence.Trim());
        (await ScalarSqlAsync<long>("SELECT count(*) FROM ai_generation WHERE beat_id = @beat", ("beat", Beat))).ShouldBe(2);
        (await ScalarSqlAsync<Guid?>("SELECT retry_of_id FROM ai_generation WHERE id = @id", ("id", Guid.Parse(second!)))).ShouldNotBeNull();
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", Guid.Parse(second!)))).ShouldBe("applied");
        (await ScalarSqlAsync<Guid>("SELECT generation_id FROM prose_revision WHERE scene_id = @scene AND source = 'ai_apply'", ("scene", Scene)))
            .ShouldBe(Guid.Parse(second!));
    }

    [Test, Order(6)]
    public async Task Step6_WhenAppliedProseEditedByHand_ShouldSaveAsPlainText()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync(SecondSentence);

        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose", "PUT", async () =>
        {
            // The beat card fills the top of the editor and has fields of its own: click in the last paragraph.
            var box = await write.ProseEditor(Scene).BoundingBoxAsync() ?? throw new InvalidOperationException("editor not on screen");
            await write.ProseEditor(Scene).ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = box.Height - 8 } });
            await Page.AfterEditorClickAsync();
            await Page.Keyboard.PressAsync("Control+End");
            await Page.Keyboard.TypeAsync(HandEdit);
            await write.Manuscript.FocusAsync();
        });

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 10_000 });
        var body = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene));
        body.ShouldNotBeNull().ShouldContain(SecondSentence + HandEdit);
        body.ShouldContain($"::beat{{id=\"{Beat}\"}}");
        // Plain text: the applied draft carries no marker of its own, and the editor shows it as ordinary paragraphs.
        body.ShouldNotContain("::draft");
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync(SecondSentence + HandEdit);
    }

    [Test, Order(7)]
    public async Task Step7_WhenFormatHideBeats_ShouldShowSceneWithoutBeatCards()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(1);
        await Expect(write.BeatCards(Scene).First).ToBeVisibleAsync();

        await write.FormatButton.ClickAsync();
        await write.FormatOption("beats", "hide").ClickAsync();

        await Expect(write.Manuscript).ToHaveAttributeAsync("data-hide-beats", "true");
        await Expect(write.BeatCards(Scene).First).ToBeHiddenAsync();
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync(SecondSentence + HandEdit);
    }
}
