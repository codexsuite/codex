using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Proofs on the seed's Players Guide chapter 1 (a missing-rendition figure and a raw-LaTeX block). The proof is an
// export_job the worker compiles, so these need pandoc, tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class ProofApiTests : ProofApiFixture
{
    private static readonly string BookBase = $"/api/books/{SeedIds.Books.PlayersGuide}";

    private static ProofRequest Chapter(bool rerender = false) =>
        new(SeedIds.Chapters.PlayersGuideCh1, Rerender: rerender);

    private async Task<(HttpStatusCode Status, ProofDto Proof)> RequestAsync(ProofRequest request)
    {
        using var response = await Client.PostAsJsonAsync($"{BookBase}/proofs", request);
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<ProofDto>())!);
    }

    private static async Task<string> CodeAsync(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        return (await response.ReadApiErrorAsync(status)).Code;
    }

    [Test, Order(1)]
    public async Task Request_WhenAChapterIsProofed_ShouldQueueAJobTheWorkerCompilesIntoAPdf()
    {
        using var chosen = await Client.PutAsJsonAsync($"{BookBase}/layout", new LayoutSelectionRequest("fantasy-rpg"));
        chosen.EnsureSuccessStatusCode();

        var (status, queued) = await RequestAsync(Chapter());
        status.ShouldBe(HttpStatusCode.Accepted);
        queued.Cached.ShouldBeFalse();
        queued.ChapterId.ShouldBe(SeedIds.Chapters.PlayersGuideCh1);
        queued.CacheKey.ShouldNotBeNullOrEmpty();

        var proof = await SettledAsync(queued.Id);

        proof.Status.ShouldBe("succeeded", proof.Log);
        (proof.TemplateSlug, proof.Trim).ShouldBe(("fantasy-rpg", "us-letter"));
        proof.PageCount.ShouldNotBeNull().ShouldBeGreaterThan(0);
        proof.Log.ShouldNotBeNullOrEmpty();
        proof.PdfUrl.ShouldBe($"/api/proofs/{proof.Id}/pdf");
        proof.CacheKey.ShouldBe(queued.CacheKey);
        var pdf = await Client.GetByteArrayAsync(proof.PdfUrl);
        pdf.Take(5).ShouldBe("%PDF-"u8.ToArray());
    }

    [Test, Order(2)]
    public async Task Request_WhenNothingChanged_ShouldAnswerWithTheCachedProofAndRerenderQueuesANewOne()
    {
        var first = (await Client.GetFromJsonAsync<ProofListDto>($"{BookBase}/proofs?chapterId={SeedIds.Chapters.PlayersGuideCh1}"))!.Proofs.Single();

        var (status, again) = await RequestAsync(Chapter());

        status.ShouldBe(HttpStatusCode.OK);
        again.Cached.ShouldBeTrue();
        again.Id.ShouldBe(first.Id);

        var (rerenderStatus, rerender) = await RequestAsync(Chapter(rerender: true));
        rerenderStatus.ShouldBe(HttpStatusCode.Accepted);
        rerender.Id.ShouldNotBe(first.Id);
        rerender.CacheKey.ShouldBe(first.CacheKey);
        (await SettledAsync(rerender.Id)).Status.ShouldBe("succeeded");
    }

    [Test, Order(3)]
    public async Task List_WhenTheChapterIsEdited_ShouldReturnACurrentKeyThatNoProofHasAnyMore()
    {
        var before = (await Client.GetFromJsonAsync<ProofListDto>($"{BookBase}/proofs?chapterId={SeedIds.Chapters.PlayersGuideCh1}"))!;
        before.Proofs.Count.ShouldBe(2);
        before.Proofs.ShouldAllBe(p => p.CacheKey == before.CurrentCacheKey);

        await AppendToFigureSceneAsync("The proof is out of date now.");

        var after = (await Client.GetFromJsonAsync<ProofListDto>($"{BookBase}/proofs?chapterId={SeedIds.Chapters.PlayersGuideCh1}"))!;
        after.CurrentCacheKey.ShouldNotBe(before.CurrentCacheKey);
        after.Proofs.ShouldAllBe(p => p.CacheKey == before.CurrentCacheKey);
    }

    [Test, Order(4)]
    public async Task Request_WhenTheChapterDoesNotCompile_ShouldFailWithTheLogAndNoPdf()
    {
        await AppendToFigureSceneAsync("```{=latex}\n\\codexUndefinedMacro\n```");

        var (_, queued) = await RequestAsync(Chapter());
        var proof = await SettledAsync(queued.Id);

        proof.Status.ShouldBe("failed");
        proof.Error.ShouldNotBeNullOrWhiteSpace();
        proof.Log.ShouldNotBeNull().ShouldContain("codexUndefinedMacro");
        proof.PdfUrl.ShouldBeNull();
        (await CodeAsync(Client.GetAsync($"/api/proofs/{proof.Id}/pdf"), HttpStatusCode.Conflict)).ShouldBe("conflict");

        var (retryStatus, retry) = await RequestAsync(Chapter());
        retryStatus.ShouldBe(HttpStatusCode.Accepted);
        retry.Id.ShouldNotBe(proof.Id);
        (await SettledAsync(retry.Id)).Status.ShouldBe("failed");
    }

    [Test, Order(5)]
    public async Task Request_WhenTheBookOrChapterIsUnknownOrTheOptionsAreBad_ShouldBeRejected()
    {
        (await CodeAsync(Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/proofs", Chapter()), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(Guid.NewGuid())), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PostAsJsonAsync($"/api/books/{SeedIds.Books.GamemastersCodex}/proofs", Chapter()), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.PostAsJsonAsync($"{BookBase}/proofs", Chapter() with { Trim = "9x9" }), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.PostAsJsonAsync($"{BookBase}/proofs", Chapter() with { Options = new Dictionary<string, JsonElement> { ["nope"] = JsonSerializer.SerializeToElement(true) } }), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.GetAsync($"{BookBase}/proofs"), HttpStatusCode.BadRequest)).ShouldBe("validation");
        (await CodeAsync(Client.GetAsync($"/api/proofs/{Guid.NewGuid()}"), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeAsync(Client.GetAsync($"/api/proofs/{Guid.NewGuid()}/pdf"), HttpStatusCode.NotFound)).ShouldBe("not_found");
    }

    private async Task AppendToFigureSceneAsync(string text)
    {
        var scene = SeedIds.Scenes.PlayersGuideFigureScene;
        var current = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{scene}/prose"))!;
        using var saved = await Client.PutAsJsonAsync($"/api/scenes/{scene}/prose", new SaveProseRequest($"{current.Body}\n\n{text}", current.Version));
        saved.EnsureSuccessStatusCode();
    }
}
