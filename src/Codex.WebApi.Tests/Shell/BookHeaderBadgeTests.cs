using System.Net.Http.Json;
using Codex.Contracts.Books;
using Codex.WebApi.Tests.KnowledgeBase;

namespace Codex.WebApi.Tests.Shell;

// The References tab badge in the book header: risks in danger, otherwise sources to check in warn.
[TestFixture]
public class BookHeaderBadgeTests : BookContentFixture
{
    [Test]
    public async Task Get_WhenOnlySourcesToCheck_ShouldBadgeThemInWarn()
    {
        var book = await AddBookAsync("Shallows");
        var scene = await AddSceneAsync(book, await AddChapterAsync(book));
        var unsigned = await AddSourceAsync(book, "Unsigned");
        var use = await AddUseAsync(book, unsigned, scene, "c", "paraphrase");
        await SetProseAsync(scene, $"[c](ref:{use})");

        var header = (await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{book}"))!;

        (header.ReferenceBadgeCount, header.ReferenceBadgeTone).ShouldBe((1, "warn"));
    }

    [Test]
    public async Task Get_WhenRiskAndSourcesToCheck_ShouldBadgeRisksInDanger()
    {
        var book = await AddBookAsync("Rapids");
        await SetQuoteLimitAsync(book, 3);
        var scene = await AddSceneAsync(book, await AddChapterAsync(book));
        var risky = await AddSourceAsync(book, "Risky", "all_rights_reserved");
        var unsigned = await AddSourceAsync(book, "Unsigned");
        var riskyUse = await AddUseAsync(book, risky, scene, "five words are quoted here");
        var unsignedUse = await AddUseAsync(book, unsigned, scene, "c", "paraphrase");
        await SetProseAsync(scene, $"[five words are quoted here](ref:{riskyUse}) [c](ref:{unsignedUse})");

        var header = (await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{book}"))!;

        (header.ReferenceBadgeCount, header.ReferenceBadgeTone).ShouldBe((1, "danger"));
    }
}
