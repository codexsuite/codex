using System.Diagnostics;
using System.Text.Json;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// The render core on the seed's Players Guide chapter: the figure scene (a low-dpi figure with no rendition files)
// and the raw-LaTeX scene. Needs pandoc, tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class RenderCoreTests : ApiFixture
{
    private static readonly RenderRequest PlayersGuideChapter =
        new(SeedIds.Books.PlayersGuide, RenderScope.Chapter, SeedIds.Chapters.PlayersGuideCh1, "fantasy-rpg");

    private IRenderService Renderer => Host.Services.GetRequiredService<IRenderService>();

    [Test, Order(1)]
    public async Task RenderPdf_WhenThePlayersGuideChapterRenders_ShouldWriteAPdfAndFlagItsRawLatexAndMissingFigure()
    {
        using var pdf = await Renderer.RenderPdfAsync(PlayersGuideChapter, null, CancellationToken.None);

        pdf.Error.ShouldBeNull(pdf.Log);
        (await File.ReadAllBytesAsync(pdf.PdfPath!)).Take(5).ShouldBe("%PDF-"u8.ToArray());
        var raw = pdf.Document.RawLatex.ShouldHaveSingleItem();
        raw.SceneId.ShouldBe(SeedIds.Scenes.PlayersGuideLatexScene);
        raw.Kept.ShouldBeTrue();
        raw.Detail.ShouldBe("\\begin{tabular}{ll}");
        var missing = pdf.Document.Flags.Where(f => f.Kind == RenderFlag.MissingAsset).ShouldHaveSingleItem();
        missing.SceneId.ShouldBe(SeedIds.Scenes.PlayersGuideFigureScene);
    }

    // #13: a one-chapter proof in under about 15 s; the first test warmed Tectonic's format and bundle cache.
    [Test, Order(2)]
    public async Task RenderPdf_WhenTheTectonicCacheIsWarm_ShouldFinishTheChapterInUnder15Seconds()
    {
        var clock = Stopwatch.StartNew();
        using var pdf = await Renderer.RenderPdfAsync(PlayersGuideChapter, null, CancellationToken.None);
        clock.Stop();

        pdf.Error.ShouldBeNull(pdf.Log);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test, Order(3)]
    public async Task RenderPdf_WhenItRuns_ShouldReportEachStepAndDeleteTheJobFolderOnDispose()
    {
        var steps = new List<string>();
        var pdf = await Renderer.RenderPdfAsync(PlayersGuideChapter, new SynchronousProgress(p => steps.Add(p.Step)), CancellationToken.None);
        var folder = pdf.JobDirectory;
        pdf.Dispose();

        steps.ShouldBe(["Assembling", "Placing images", "Converting", "Typesetting", "Done"]);
        Directory.Exists(folder).ShouldBeFalse();
    }

    [Test, Order(4)]
    public async Task Assemble_WhenTheTemplateDoesNotAllowRawLatex_ShouldDropTheBlockButStillFlagIt()
    {
        var document = await Renderer.AssembleAsync(PlayersGuideChapter with { Template = "manuscript" }, RenderTarget.Pdf, CancellationToken.None);

        document.RawLatex.ShouldHaveSingleItem().Kept.ShouldBeFalse();
        document.Ast.ToJsonString().ShouldNotContain("tabular");
        document.TemplateSlug.ShouldBe("manuscript");
        document.Trim.ShouldBe("us-letter");
    }

    [Test, Order(5)]
    public async Task Assemble_WhenTheSameChapterIsAssembledTwice_ShouldGiveTheSameContentHash()
    {
        var first = await Renderer.AssembleAsync(PlayersGuideChapter, RenderTarget.Pdf, CancellationToken.None);
        var second = await Renderer.AssembleAsync(PlayersGuideChapter, RenderTarget.Pdf, CancellationToken.None);
        var otherTrim = await Renderer.AssembleAsync(PlayersGuideChapter with { Trim = "a4" }, RenderTarget.Pdf, CancellationToken.None);

        second.ContentHash.ShouldBe(first.ContentHash);
        otherTrim.ContentHash.ShouldNotBe(first.ContentHash);
    }

    [Test, Order(6)]
    public async Task RenderHtml_WhenTheChapterRenders_ShouldGiveAFragmentAndThePreviewCss()
    {
        var html = await Renderer.RenderHtmlAsync(PlayersGuideChapter, CancellationToken.None);

        html.Html.ShouldContain("Welcome to the Fallen World");
        html.Html.ShouldContain("missing-figure");
        html.Html.ShouldNotContain("tabular");
        html.PreviewCss.ShouldNotBeNull();
    }

    [Test, Order(7)]
    public async Task Resolve_WhenTheRequestNamesAnOptionTheTemplateLacks_ShouldBeAValidationError()
    {
        var request = PlayersGuideChapter with { Options = new Dictionary<string, JsonElement> { ["columns"] = JsonSerializer.SerializeToElement(3) } };

        var error = await Should.ThrowAsync<ApiException>(() => Renderer.AssembleAsync(request, RenderTarget.Pdf, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(8)]
    public async Task Resolve_WhenTheTrimIsNotOneTheTemplateOffers_ShouldBeAValidationError()
    {
        var error = await Should.ThrowAsync<ApiException>(() =>
            Renderer.AssembleAsync(PlayersGuideChapter with { Trim = "6x9" }, RenderTarget.Pdf, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(9)]
    public async Task Assemble_WhenTheTemplateIsUnknown_ShouldBeNotFound()
    {
        var error = await Should.ThrowAsync<ApiException>(() =>
            Renderer.AssembleAsync(PlayersGuideChapter with { Template = "no-such-template" }, RenderTarget.Pdf, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(10)]
    public async Task Assemble_WhenTheSceneIsInAnotherBook_ShouldBeNotFound()
    {
        var request = new RenderRequest(SeedIds.Books.PlayersGuide, RenderScope.Scene, SeedIds.Scenes.Ch3Sc1);

        var error = await Should.ThrowAsync<ApiException>(() => Renderer.AssembleAsync(request, RenderTarget.Pdf, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    // Progress<T> posts to the thread pool; the test needs the steps in order as they were reported.
    private sealed class SynchronousProgress(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }
}
