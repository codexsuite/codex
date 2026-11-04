using Codex.AcceptanceTests.Harness;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// Base of the clearance scenarios: the seed clone, the book they run on, and a way to read a scene's prose back.
public abstract class ReferencesLaneTest : SeededAcceptanceTest
{
    protected static string Book => SeedIds.Books.ProductEngineering.ToString();

    protected static string SourceLink(Guid sourceId) => Links.References(Book, Links.Hash((sourceId.ToString(), null)));

    protected async Task<string> ProseBodyAsync(Guid sceneId)
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/scenes/{sceneId}/prose");
        response.Status.ShouldBe(200);
        return (await response.JsonAsync())!.Value.GetProperty("body").GetString()!;
    }
}
