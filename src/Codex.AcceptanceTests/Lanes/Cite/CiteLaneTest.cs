using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Lanes.Write;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Cite;

// Base class of the cite scenarios: Write at Ch 7 Sc 1 on the seed, the scene's prose read through the API, and
// citing one of its sentences from the `/` menu.
public abstract class CiteLaneTest : WriteLaneTest
{
    protected static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    protected static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected async Task<ProseDto> GetProseAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose", Json))!;
    }

    // The first sentence of the n-th paragraph (the scene's filler paragraphs hold 12-word sentences).
    protected static string Sentence(string body, int paragraph)
    {
        var text = body.Split("\n\n")[paragraph];
        return text[..(text.IndexOf('.') + 1)];
    }

    protected async Task<WritePageObject> OpenAsync()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "7.1"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        return write;
    }

    // Cites the first sentence of the n-th paragraph for `sourceId` through the picker and returns the server's answer.
    protected async Task<CitePassageResult> CiteAsync(Guid sourceId, int paragraph)
    {
        var sentence = Sentence((await GetProseAsync()).Body, paragraph);
        var cite = new CitePage(Page);
        var response = await Page.RunAndWaitForApiAsync("/api/citations", "POST", () => cite.CiteAsync(Scene, sentence, sourceId));
        response.Ok.ShouldBeTrue();
        var result = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), Json)!;
        result.Body.ShouldContain($"[{sentence}](ref:{result.UseId})");
        return result;
    }
}
