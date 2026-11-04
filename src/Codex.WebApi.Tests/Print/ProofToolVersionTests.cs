using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// In production the web image has no TeX toolchain and the worker image has it, so the two processes hash the same
// chapter with different tool versions. The test host runs both in one process, so this fixture makes the worker's
// hash differ: the render service it registers adds a tool suffix to the content hash of every worker request
// (the worker names the template version, the web process does not). A finished proof must still carry the key the
// web process computed, or the Layout screen reads it as out of date. Needs the toolchain on PATH, as CI installs it.
[TestFixture]
public class ProofToolVersionTests : ProofApiFixture
{
    private static readonly string BookBase = $"/api/books/{SeedIds.Books.PlayersGuide}";

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
        services.AddSingleton<IRenderService>(sp => new WorkerToolsRenderService(ActivatorUtilities.CreateInstance<RenderService>(sp)));

    [Test]
    public async Task Proof_WhenTheWorkerHasOtherToolVersionsThanTheWebProcess_ShouldKeepTheRequestKeyAndStayCurrent()
    {
        var chapter = SeedIds.Chapters.PlayersGuideCh1;
        using var response = await Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(chapter));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await response.Content.ReadFromJsonAsync<ProofDto>()).ShouldNotBeNull();

        var proof = await SettledAsync(queued.Id);

        proof.Status.ShouldBe("succeeded", proof.Log);
        var list = (await Client.GetFromJsonAsync<ProofListDto>($"{BookBase}/proofs?chapterId={chapter}")).ShouldNotBeNull();
        proof.CacheKey.ShouldBe(queued.CacheKey);
        list.CurrentCacheKey.ShouldBe(queued.CacheKey);
        list.Proofs.Single().CacheKey.ShouldBe(list.CurrentCacheKey);

        using var again = await Client.PostAsJsonAsync($"{BookBase}/proofs", new ProofRequest(chapter));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cached = (await again.Content.ReadFromJsonAsync<ProofDto>()).ShouldNotBeNull();
        (cached.Cached, cached.Id).ShouldBe((true, proof.Id));
    }

    // Behaves as RenderService, except that a worker request (one that names the template version) hashes with other tools.
    private sealed class WorkerToolsRenderService(IRenderService inner) : IRenderService
    {
        public Task<(TemplatePackage Template, string Trim, IReadOnlyDictionary<string, JsonElement> Options)> ResolveAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.ResolveAsync(request, cancellationToken);

        public Task<ResolvedLayout> ResolveLayoutAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.ResolveLayoutAsync(request, cancellationToken);

        public async Task<AssembledDocument> AssembleAsync(RenderRequest request, RenderTarget target, CancellationToken cancellationToken)
        {
            var document = await inner.AssembleAsync(request, target, cancellationToken);
            return request.TemplateVersionId is null ? document : document with { ContentHash = document.ContentHash + "|worker-tools" };
        }

        public Task<RenderedPdf> RenderPdfAsync(RenderRequest request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken) =>
            inner.RenderPdfAsync(request, progress, cancellationToken);

        public Task<RenderedPdf> CompileAsync(AssembledDocument document, IProgress<RenderProgress>? progress, CancellationToken cancellationToken, PrintFinish? finish = null) =>
            inner.CompileAsync(document, progress, cancellationToken, finish);

        public Task<RenderedHtml> RenderHtmlAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.RenderHtmlAsync(request, cancellationToken);
    }
}
