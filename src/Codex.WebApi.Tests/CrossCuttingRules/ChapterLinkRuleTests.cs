using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.CrossCuttingRules;

// Cross-cutting rule 1 (docs/journeys-wave5.md): a `[Title](chapter:<id>)` link is never rewritten when its target goes
// away. Archiving the act drops the chapter from the outline, which the client reads as "Missing chapter"; restoring the
// act brings the chapter, and the link's live number, back. Archiving the chapter alone (Delete chapter) keeps it in the
// outline as archived with its title, which the client draws as the title in an archived style that opens Plan's
// Archived list. Chapter 5 is linked from Ch 2 Sc 1.
[TestFixture]
public class ChapterLinkRuleTests : ApiFixture
{
    private static readonly string Link = $"(chapter:{SeedIds.Chapters.Ch5})";
    private string _body = "";
    private int? _numberBefore;

    private async Task<OutlineChapterDto?> Ch5Async() =>
        (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{SeedIds.Books.ProductEngineering}/outline", JsonSerializerOptions.Web))!
            .Acts.SelectMany(a => a.Chapters).SingleOrDefault(c => c.Id == SeedIds.Chapters.Ch5);

    private async Task<string> LinkingBodyAsync() =>
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch2Sc1}/prose", JsonSerializerOptions.Web))!.Body;

    [Test, Order(1)]
    public async Task ArchiveAct_WhenAChapterInItIsLinked_ShouldLeaveTheLinkInTheProseAndDropTheChapterFromTheOutline()
    {
        _body = await LinkingBodyAsync();
        _body.ShouldContain(Link);
        _numberBefore = (await Ch5Async())!.Number;
        _numberBefore.ShouldBe(5);

        using var response = await Client.PostAsync($"/api/acts/{SeedIds.Acts.TheProductEngineer}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LinkingBodyAsync()).ShouldBe(_body);
        (await Ch5Async()).ShouldBeNull();
    }

    [Test, Order(2)]
    public async Task RestoreAct_WhenItsChapterWasLinked_ShouldBringTheChapterAndItsNumberBack()
    {
        using var response = await Client.PostAsync($"/api/acts/{SeedIds.Acts.TheProductEngineer}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await Ch5Async())!;
        (chapter.Archived, chapter.Number).ShouldBe((false, _numberBefore));
        (await LinkingBodyAsync()).ShouldBe(_body);
    }

    [Test, Order(3)]
    public async Task ArchiveChapter_WhenItIsLinked_ShouldKeepTheLinkAndTheTitleAndMarkTheChapterArchived()
    {
        var title = (await Ch5Async())!.Title;

        using var response = await Client.PostAsync($"/api/chapters/{SeedIds.Chapters.Ch5}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LinkingBodyAsync()).ShouldBe(_body);
        var chapter = (await Ch5Async())!;
        (chapter.Archived, chapter.Number, chapter.Title).ShouldBe((true, null, title));
    }

    [Test, Order(4)]
    public async Task RestoreChapter_WhenItWasLinked_ShouldBringItsNumberBack()
    {
        using var response = await Client.PostAsync($"/api/chapters/{SeedIds.Chapters.Ch5}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await Ch5Async())!;
        (chapter.Archived, chapter.Number).ShouldBe((false, _numberBefore));
        (await LinkingBodyAsync()).ShouldBe(_body);
    }
}
