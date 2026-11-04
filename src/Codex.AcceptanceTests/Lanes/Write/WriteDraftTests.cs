using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.Write;

// The AI draft panel on the seed's Ch 5 Sc 2 beat: the pending draft, retry, stop, discard with undo, and a failure.
[TestFixture]
public class WriteDraftTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc2;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc2OutcomeMetrics;
    private static readonly SwitchableProseGenerator Generator = new();
    private static readonly TaskCompletionSource Release = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(Page.GetByTestId(WriteTestIds.Scene(Scene))).ToBeVisibleAsync();
    }

    [Test, Order(1)]
    public async Task PendingDraft_WhenApplied_ShouldWriteRevision()
    {
        var write = new WritePageObject(Page);
        await OpenAsync();
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToContainTextAsync("Outcome metrics rot");

        var response = await Page.RunAndWaitForApiAsync($"/api/generations/{SeedIds.AiGenerations.Ch5Sc2Pending}/apply", "POST", async () =>
            await write.DraftApply(Beat).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("Outcome metrics rot");
        await Expect(write.Toast).ToContainTextAsync("Applied");
        (await ScalarSqlAsync<Guid>(
            "SELECT generation_id FROM prose_revision WHERE scene_id = @scene AND source = 'ai_apply'", ("scene", Scene)))
            .ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", SeedIds.AiGenerations.Ch5Sc2Pending))).ShouldBe("applied");
    }

    [Test, Order(2)]
    public async Task Retry_WhenApplied_ShouldUseAttemptTwo()
    {
        var write = new WritePageObject(Page);
        Generator.Current = new FakeProseGenerator(["A retried opening. ", "And its second sentence."]);
        await OpenAsync();

        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", async () =>
            await write.BeatGenerate(Beat).ClickAsync());
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftMeta(Beat)).Not.ToContainTextAsync("attempt");
        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", async () =>
            await write.DraftRetry(Beat).ClickAsync());
        await Expect(write.DraftMeta(Beat)).ToContainTextAsync("attempt 2");
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        var retried = await write.Draft(Beat).GetAttributeAsync("data-generation-id");
        await Page.RunAndWaitForApiAsync($"/api/generations/{retried}/apply", "POST", async () =>
            await write.DraftApply(Beat).ClickAsync());

        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("A retried opening. And its second sentence.");
        (await ScalarSqlAsync<int>(
            "SELECT (g.request->>'attempt')::int FROM prose_revision r JOIN ai_generation g ON g.id = r.generation_id WHERE r.scene_id = @scene AND g.id <> @seed AND r.source = 'ai_apply'",
            ("scene", Scene), ("seed", SeedIds.AiGenerations.Ch5Sc2Pending))).ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Stop_ShouldKeepPartial()
    {
        var write = new WritePageObject(Page);
        Generator.Current = new FakeProseGenerator(["Half a draft. ", "The rest of it."], Release.Task);
        await OpenAsync();

        await write.BeatGenerate(Beat).ClickAsync();
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "streaming");
        await Expect(write.DraftText(Beat)).ToHaveTextAsync("Half a draft.");
        await write.DraftStop(Beat).ClickAsync();

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToHaveTextAsync("Half a draft.");
        await Expect(write.DraftApply(Beat)).ToBeVisibleAsync();
        await Expect(write.DraftRetry(Beat)).ToBeVisibleAsync();
        Release.SetResult();
        await OpenAsync();
        await Expect(write.DraftText(Beat)).ToHaveTextAsync("Half a draft.");
    }

    [Test, Order(4)]
    public async Task Discard_ThenUndo_ShouldRestore()
    {
        var write = new WritePageObject(Page);
        await OpenAsync();
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");

        var id = await write.Draft(Beat).GetAttributeAsync("data-generation-id");
        await Page.RunAndWaitForApiAsync($"/api/generations/{id}/discard", "POST", async () =>
            await write.DraftDiscard(Beat).ClickAsync());
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", Guid.Parse(id!)))).ShouldBe("discarded");
        await Page.RunAndWaitForApiAsync($"/api/generations/{id}/undo-discard", "POST", async () =>
            await write.ToastUndo.ClickAsync());

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToHaveTextAsync("Half a draft.");
    }

    [Test, Order(5)]
    public async Task Generate_WhenGeneratorFails_ShouldShowMessage()
    {
        var write = new WritePageObject(Page);
        Generator.Current = new FakeProseGenerator(["Some words. "], null, new InvalidOperationException("provider down"));
        await OpenAsync();

        await write.BeatGenerate(Beat).ClickAsync();

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "failed");
        await Expect(write.DraftError(Beat)).ToHaveTextAsync("The draft could not be generated.");
        await Expect(write.DraftRetry(Beat)).ToBeVisibleAsync();
    }
}
