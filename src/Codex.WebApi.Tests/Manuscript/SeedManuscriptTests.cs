using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Manuscript;

// The manuscript and the outline are two requests the Write screen joins by scene id, so they must agree on order.
[TestFixture]
public class SeedManuscriptTests : ApiFixture
{
    [Test]
    public async Task Get_WhenProductEngineeringSeeded_ShouldListTheOutlinesScenesInOrder()
    {
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{SeedIds.Books.ProductEngineering}/outline", JsonSerializerOptions.Web);
        var manuscript = await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{SeedIds.Books.ProductEngineering}/manuscript", JsonSerializerOptions.Web);

        var expected = outline!.Acts.SelectMany(a => a.Chapters).Where(c => !c.Archived).SelectMany(c => c.Scenes).Select(s => s.Id);
        manuscript!.Scenes.Select(s => s.SceneId).ShouldBe(expected);
        manuscript.Scenes.Count.ShouldBe(15);
    }

    [Test]
    public async Task Get_WhenHymnsSeeded_ShouldSkipTheArchivedChapter()
    {
        var manuscript = await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{SeedIds.Books.HymnsForMachines}/manuscript", JsonSerializerOptions.Web);

        manuscript!.Scenes.Count.ShouldBe(5);
        manuscript.Scenes.Select(s => s.SceneId).ShouldContain(SeedIds.Scenes.HymnsSc1);
    }
}
