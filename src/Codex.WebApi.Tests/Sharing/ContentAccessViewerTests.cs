using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Sharing;

// The access rule over content (#80): Ana has view on Product Engineering (seed 09_sharing). Every tab's reads answer
// 200; every change answers 403 forbidden, checked before the body, so the bodies here are empty or minimal.
[TestFixture]
public class ContentAccessViewerTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;

    protected override TestUser SignedInAs => TestUser.Of(SeedIds.Users.Ana);

    private static IEnumerable<TestCaseData> Reads()
    {
        yield return new TestCaseData($"/api/books/{Book}/manuscript").SetArgDisplayNames("manuscript");
        yield return new TestCaseData($"/api/books/{Book}/outline").SetArgDisplayNames("plan");
        yield return new TestCaseData($"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose").SetArgDisplayNames("prose");
        yield return new TestCaseData($"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose/revisions").SetArgDisplayNames("prose history");
        yield return new TestCaseData($"/api/books/{Book}/kb").SetArgDisplayNames("knowledge base");
        yield return new TestCaseData($"/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}").SetArgDisplayNames("kb entry");
        yield return new TestCaseData($"/api/books/{Book}/sources").SetArgDisplayNames("sources");
        yield return new TestCaseData($"/api/references/{SeedIds.References.BeckXp}").SetArgDisplayNames("source");
        yield return new TestCaseData($"/api/books/{Book}/markers").SetArgDisplayNames("markers");
        yield return new TestCaseData($"/api/books/{Book}/quote-flags").SetArgDisplayNames("quote flags");
        yield return new TestCaseData($"/api/books/{Book}/drafts").SetArgDisplayNames("drafts");
        yield return new TestCaseData($"/api/books/{Book}/assets").SetArgDisplayNames("images");
    }

    private static IEnumerable<TestCaseData> Writes()
    {
        var scene = SeedIds.Scenes.Ch3Sc1;
        yield return new TestCaseData("PUT", $"/api/scenes/{scene}/prose").SetArgDisplayNames("prose save");
        yield return new TestCaseData("PATCH", $"/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}").SetArgDisplayNames("kb entry edit");
        yield return new TestCaseData("POST", $"/api/books/{Book}/kb/entries").SetArgDisplayNames("kb entry create");
        yield return new TestCaseData("PATCH", $"/api/references/{SeedIds.References.BeckXp}").SetArgDisplayNames("reference edit");
        yield return new TestCaseData("POST", $"/api/references/{SeedIds.References.BeckXp}/sign-off").SetArgDisplayNames("reference sign-off");
        yield return new TestCaseData("PATCH", $"/api/scenes/{scene}").SetArgDisplayNames("plan scene edit");
        yield return new TestCaseData("POST", $"/api/books/{Book}/acts").SetArgDisplayNames("plan act create");
        yield return new TestCaseData("POST", $"/api/beats/{SeedIds.Beats.Ch3Sc1EarlyAgileYears}/generate").SetArgDisplayNames("draft");
        yield return new TestCaseData("POST", $"/api/generations/{SeedIds.AiGenerations.Ch5Sc2Pending}/discard").SetArgDisplayNames("draft discard");
        yield return new TestCaseData("PUT", $"/api/books/{Book}/layout").SetArgDisplayNames("layout choice");
        yield return new TestCaseData("POST", $"/api/books/{Book}/export/preflight").SetArgDisplayNames("export preflight");
        yield return new TestCaseData("POST", $"/api/books/{Book}/exports").SetArgDisplayNames("export");
        yield return new TestCaseData("GET", $"/api/books/{Book}/exports").SetArgDisplayNames("export list");
        yield return new TestCaseData("POST", $"/api/books/{Book}/manuscript-exports").SetArgDisplayNames("manuscript export");
        yield return new TestCaseData("POST", $"/api/books/{Book}/proofs").SetArgDisplayNames("proof");
    }

    [TestCaseSource(nameof(Reads))]
    public async Task Get_WhenTheBookIsSharedToView_ShouldReturn200(string url)
    {
        using var response = await Client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
    }

    [TestCaseSource(nameof(Writes))]
    public async Task Change_WhenTheBookIsSharedToView_ShouldReturn403Forbidden(string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != "GET")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await Client.SendAsync(request);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Forbidden);
        error.Code.ShouldBe(ApiErrorCodes.Forbidden);
        error.Error.ShouldBe("This book is shared with you to view only.");
    }

    [Test]
    public async Task SaveProse_WhenTheBookIsSharedToView_ShouldLeaveTheProseUnchanged()
    {
        var before = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose"))!;

        using var response = await Client.PutAsJsonAsync(
            $"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose", new SaveProseRequest("Overwritten by a viewer.", before.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
        var after = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose"))!;
        (after.Body, after.Version).ShouldBe((before.Body, before.Version));
    }

    [Test]
    public async Task UploadImage_WhenTheBookIsSharedToView_ShouldReturn403Forbidden()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Print.TestImages.Photo(40, 30)), "file", "plate.png");

        using var response = await Client.PostAsync($"/api/books/{Book}/assets", form);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Forbidden);
        (error.Code, error.Error).ShouldBe((ApiErrorCodes.Forbidden, "This book is shared with you to view only."));
    }
}
