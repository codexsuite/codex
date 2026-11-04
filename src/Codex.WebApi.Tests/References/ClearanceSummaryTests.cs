using System.Net.Http.Json;
using Codex.Contracts.References;
using Codex.WebApi.Tests.KnowledgeBase;

namespace Codex.WebApi.Tests.References;

[TestFixture]
public class ClearanceSummaryTests : BookContentFixture
{
    private Guid _book;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        await SetQuoteLimitAsync(_book, 3);
        var scene = await AddSceneAsync(_book, await AddChapterAsync(_book));
        var risky = await AddSourceAsync(_book, "Risky", "all_rights_reserved");
        var clearedOne = await AddSourceAsync(_book, "ClearedOne");
        var clearedTwo = await AddSourceAsync(_book, "ClearedTwo");
        var unsigned = await AddSourceAsync(_book, "Unsigned");
        await AddSourceAsync(_book, "Uncited");
        var riskyUse = await AddUseAsync(_book, risky, scene, "five words are quoted here");
        var oneUse = await AddUseAsync(_book, clearedOne, scene, "a", "paraphrase");
        var twoUse = await AddUseAsync(_book, clearedTwo, scene, "b", "paraphrase");
        var unsignedUse = await AddUseAsync(_book, unsigned, scene, "c", "paraphrase");
        await SetProseAsync(
            scene,
            $"[five words are quoted here](ref:{riskyUse}) [a](ref:{oneUse}) [b](ref:{twoUse}) [c](ref:{unsignedUse})");
        await SignOffAsync(clearedOne);
        await SignOffAsync(clearedTwo);
    }

    [Test]
    public async Task Get_ShouldCountFromView()
    {
        var summary = (await Client.GetFromJsonAsync<ClearanceSummaryDto>($"/api/books/{_book}/references/summary"))!;

        summary.ShouldBe(new ClearanceSummaryDto(Cleared: 2, Total: 5, Risks: 1, NeedsCheck: 1, NotCited: 1, Badge: 1, RemindersDue: 0));
    }

    [Test]
    public async Task Get_WhenNoRisks_ShouldBadgeSourcesToCheck()
    {
        var book = await AddBookAsync("Shallows");
        var scene = await AddSceneAsync(book, await AddChapterAsync(book));
        var unsigned = await AddSourceAsync(book, "Unsigned");
        var use = await AddUseAsync(book, unsigned, scene, "c", "paraphrase");
        await SetProseAsync(scene, $"[c](ref:{use})");

        var summary = (await Client.GetFromJsonAsync<ClearanceSummaryDto>($"/api/books/{book}/references/summary"))!;

        summary.ShouldBe(new ClearanceSummaryDto(Cleared: 0, Total: 1, Risks: 0, NeedsCheck: 1, NotCited: 0, Badge: 1, RemindersDue: 0));
    }

    [Test]
    public async Task Get_WhenUnknownBook_ShouldReturnNotFound()
    {
        (await ApiErrors.ReadApiErrorAsync(
            await Client.GetAsync($"/api/books/{Guid.NewGuid()}/references/summary"), System.Net.HttpStatusCode.NotFound))
            .Code.ShouldBe("not_found");
    }
}
