using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.Write;

// A draft for a beat that already has prose (Ch 5 Sc 1, first beat): the note counts the words, Apply replaces them,
// the old text stays in history, and Undo apply brings it back.
[TestFixture]
public class WriteDraftReplaceTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc1EngineersInDiscovery;
    private static readonly Guid NextBeat = SeedIds.Beats.Ch5Sc1WhatChanged;
    private static readonly FakeProseGenerator Generator = new(["A fresh opening. ", "And a second sentence."]);

    private string _oldBody = "";
    private string _oldProse = "";

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    [Test, Order(1)]
    public async Task PendingDraft_WhenBeatHasProse_ShouldReplaceItAndKeepHistory()
    {
        var write = new WritePageObject(Page);
        _oldBody = (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene)))!;
        var from = _oldBody.IndexOf($"::beat{{id=\"{Beat}\"}}", StringComparison.Ordinal);
        var to = _oldBody.IndexOf($"::beat{{id=\"{NextBeat}\"}}", StringComparison.Ordinal);
        _oldProse = _oldBody[(from + 50)..to];
        var oldWords = _oldProse.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var firstOldLine = _oldProse.Trim().Split('\n')[0];
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(Page.GetByTestId(WriteTestIds.Scene(Scene))).ToBeVisibleAsync();

        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", async () =>
            await write.BeatGenerate(Beat).ClickAsync());
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");

        await Expect(write.DraftNote(Beat)).ToHaveTextAsync($"Apply replaces the {oldWords:N0} words below. The current text is kept in history.");
        var id = await write.Draft(Beat).GetAttributeAsync("data-generation-id");
        var response = await Page.RunAndWaitForApiAsync($"/api/generations/{id}/apply", "POST", async () =>
            await write.DraftApply(Beat).ClickAsync());
        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("A fresh opening. And a second sentence.");
        await Expect(write.ProseEditor(Scene)).Not.ToContainTextAsync(firstOldLine[..40]);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene)))!
            .ShouldContain($"::beat{{id=\"{NextBeat}\"}}");
        (await ScalarSqlAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene AND body = @body", ("scene", Scene), ("body", _oldBody)))
            .ShouldBe(1);

        await Page.RunAndWaitForApiAsync($"/api/generations/{id}/undo-apply", "POST", async () =>
            await write.ToastUndo.ClickAsync());

        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene))).ShouldBe(_oldBody);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync(firstOldLine[..40]);
    }
}
