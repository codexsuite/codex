using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.CrossCuttingRules;

// Cross-cutting rule 6 (the 0-chapter book): Northbound has the one untitled act the insert trigger makes and nothing
// else. Every read the Plan, Write and References screens make answers 200 with empty lists, never an error.
[TestFixture]
public class ZeroChapterBookRuleTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.Northbound;

    [Test]
    public async Task Reads_WhenTheBookHasNoChapters_ShouldAnswerEmptyAndNotFail()
    {
        var outline = (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{Book}/outline", JsonSerializerOptions.Web))!;
        outline.Acts.Select(a => a.Id).ShouldBe([SeedIds.Acts.NorthboundUntitled]);
        outline.Acts.SelectMany(a => a.Chapters).ShouldBeEmpty();

        (await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{Book}/manuscript", JsonSerializerOptions.Web))!.Scenes.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<SourceRowDto[]>($"/api/books/{Book}/sources", JsonSerializerOptions.Web))!.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<SourceCslDto[]>($"/api/books/{Book}/sources/csl", JsonSerializerOptions.Web))!.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<ClearanceSummaryDto>($"/api/books/{Book}/references/summary", JsonSerializerOptions.Web))!.Total.ShouldBe(0);
    }
}
