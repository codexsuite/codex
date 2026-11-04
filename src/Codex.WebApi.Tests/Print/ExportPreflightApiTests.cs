using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Preflight and the request rules of Export for printer on two seed books: the Players Guide (an image with no rendition,
// a low-dpi map, a raw-LaTeX table) and Product Engineering (three copyright risks). Nothing here compiles.
[TestFixture]
public class ExportPreflightApiTests : ApiFixture
{
    private static readonly string Guide = $"/api/books/{SeedIds.Books.PlayersGuide}";
    private static readonly string Risky = $"/api/books/{SeedIds.Books.ProductEngineering}";

    private async Task<PreflightDto> PreflightAsync(string book, ExportRequest? request = null)
    {
        using var response = await Client.PostAsJsonAsync($"{book}/export/preflight", request ?? new ExportRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PreflightDto>())!;
    }

    [Test, Order(1)]
    public async Task Preflight_WhenAnImageHasNoFile_ShouldBlockAndWarnAboutLowDpiAndNoteKeptRawLatex()
    {
        var preflight = await PreflightAsync(Guide);

        preflight.Verdict.ShouldBe("blocked");
        preflight.Blocks.Select(b => b.Kind).ShouldBe(["missing_asset"]);
        preflight.Blocks[0].SceneId.ShouldBe(SeedIds.Scenes.PlayersGuideFigureScene);
        preflight.Warnings.ShouldContain(w => w.Kind == "low_dpi" && w.Detail.Contains("fallen-world-map.png"));
        preflight.Notes.ShouldContain(n => n.Kind == "raw_latex");
        (preflight.Template, preflight.Trim).ShouldBe(("novel-6x9", "6x9"));
    }

    [Test, Order(2)]
    public async Task Preflight_WhenSourcesNeedPermission_ShouldBlockOnEachCopyrightRisk()
    {
        var preflight = await PreflightAsync(Risky);

        preflight.Verdict.ShouldBe("blocked");
        preflight.Blocks.Count(b => b.Kind == "copyright_risk" && b.ReferenceId is not null).ShouldBe(3);
    }

    [Test, Order(3)]
    public async Task Preflight_WhenTheTargetIsScreenQuality_ShouldNameTheScreenTargetInItsDpiWarning()
    {
        var preflight = await PreflightAsync(Guide, new ExportRequest(Quality: "screen"));

        preflight.Warnings.ShouldContain(w => w.Kind == "low_dpi" && w.Detail.EndsWith("below 150", StringComparison.Ordinal));
    }

    [Test, Order(4)]
    public async Task Request_WhenPreflightBlocksAndItIsNotADraft_ShouldBeRefusedAndQueueNothing()
    {
        using var response = await Client.PostAsJsonAsync($"{Risky}/exports", new ExportRequest());

        (await response.ReadApiErrorAsync(HttpStatusCode.UnprocessableEntity)).Code.ShouldBe(ApiErrorCodes.Blocked);
        var list = (await Client.GetFromJsonAsync<ExportListDto>($"{Risky}/exports"))!;
        list.Exports.ShouldBeEmpty();
        list.Running.ShouldBeNull();
    }

    [TestCase("1in", false, "print")]
    [TestCase("none", true, "print")]
    [TestCase("3mm", false, "draft")]
    public async Task Request_WhenASettingIsWrong_ShouldBeAValidationError(string bleed, bool crop, string quality)
    {
        using var response = await Client.PostAsJsonAsync($"{Guide}/exports", new ExportRequest(Bleed: bleed, CropMarks: crop, Quality: quality, Draft: true));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task Request_WhenTheFormatIsNotPdf_ShouldBeAValidationError()
    {
        using var response = await Client.PostAsJsonAsync($"{Guide}/exports", new ExportRequest(Format: "epub", Draft: true));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(6)]
    public async Task Endpoints_WhenTheBookOrExportIsUnknown_ShouldAnswerNotFound()
    {
        var unknown = Guid.NewGuid();
        using var preflight = await Client.PostAsJsonAsync($"/api/books/{unknown}/export/preflight", new ExportRequest());
        using var queue = await Client.PostAsJsonAsync($"/api/books/{unknown}/exports", new ExportRequest(Draft: true));
        using var list = await Client.GetAsync($"/api/books/{unknown}/exports");
        using var get = await Client.GetAsync($"/api/exports/{unknown}");
        using var file = await Client.GetAsync($"/api/exports/{unknown}/file");
        using var cancel = await Client.PostAsync($"/api/exports/{unknown}/cancel", null);

        foreach (var response in new[] { preflight, queue, list, get, file, cancel })
        {
            (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        }
    }
}
