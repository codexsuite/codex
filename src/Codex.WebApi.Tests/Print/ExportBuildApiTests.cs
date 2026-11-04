using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Export for printer on the seed's Players Guide as a draft (its image has no file, so a final export is blocked):
// the worker builds the whole book with bleed, crop marks and the DRAFT watermark, the job is listed with its
// settings, the PDF downloads, a second request while one runs is a conflict, and a running export can be cancelled.
// Needs pandoc, tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class ExportBuildApiTests : ExportApiFixture
{
    private static readonly string Guide = $"/api/books/{SeedIds.Books.PlayersGuide}";

    private static readonly ExportRequest Draft = new(Bleed: "3mm", CropMarks: true, Quality: "screen", Draft: true);

    private Guid _built;

    [Test, Order(1)]
    public async Task Request_WhenADraftIsQueued_ShouldBuildThePdfWithItsSettings()
    {
        using var response = await Client.PostAsJsonAsync($"{Guide}/exports", Draft);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await response.Content.ReadFromJsonAsync<ExportDto>())!;
        (queued.Kind, queued.Draft, queued.Bleed, queued.CropMarks, queued.Quality).ShouldBe(("print", true, "3mm", true, "screen"));
        queued.Status.ShouldBeOneOf("queued", "running");
        queued.Blocked.ShouldBe(1);

        var export = await SettledAsync(queued.Id);

        export.Status.ShouldBe("succeeded", export.Error ?? export.Log);
        export.PageCount.ShouldNotBeNull().ShouldBeGreaterThan(0);
        export.Bytes.ShouldNotBeNull().ShouldBeGreaterThan(0);
        export.Log.ShouldNotBeNullOrEmpty();
        export.FileUrl.ShouldBe($"/api/exports/{export.Id}/file");
        (export.Template, export.TemplateVersion, export.Trim).ShouldBe(("novel-6x9", "1.0.0", "6x9"));
        var pdf = await Client.GetByteArrayAsync(export.FileUrl);
        pdf.Take(5).ShouldBe("%PDF-"u8.ToArray());
        _built = export.Id;
    }

    [Test, Order(2)]
    public async Task List_WhenAnExportFinished_ShouldListItWithItsSettingsNewestFirst()
    {
        var list = (await Client.GetFromJsonAsync<ExportListDto>($"{Guide}/exports"))!;

        list.Running.ShouldBeNull();
        var export = list.Exports.ShouldHaveSingleItem();
        export.Id.ShouldBe(_built);
        export.CreatedLabel.ShouldNotBeNullOrEmpty();
        export.Log.ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task Request_WhileAnotherIsRunning_ShouldConflictAndCancelStopsTheFirst()
    {
        using var first = await Client.PostAsJsonAsync($"{Guide}/exports", Draft);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await first.Content.ReadFromJsonAsync<ExportDto>())!;

        using var second = await Client.PostAsJsonAsync($"{Guide}/exports", Draft);
        (await second.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await Client.GetFromJsonAsync<ExportListDto>($"{Guide}/exports"))!.Running.ShouldBe(queued.Id);

        using var cancel = await Client.PostAsync($"/api/exports/{queued.Id}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        (await cancel.Content.ReadFromJsonAsync<ExportDto>())!.Status.ShouldBe("cancelled");
        using var again = await Client.PostAsync($"/api/exports/{queued.Id}/cancel", null);
        (await again.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        using var file = await Client.GetAsync($"/api/exports/{queued.Id}/file");
        (await file.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        var settled = await SettledAsync(queued.Id);
        settled.Status.ShouldBe("cancelled");
        settled.FileUrl.ShouldBeNull();
    }

    [Test, Order(4)]
    public async Task Request_WhenTheTemplateCannotTakeABleed_ShouldFailTheExportWithItsReason()
    {
        using var chosen = await Client.PutAsJsonAsync($"{Guide}/layout", new LayoutSelectionRequest("manuscript"));
        chosen.EnsureSuccessStatusCode();

        using var response = await Client.PostAsJsonAsync($"{Guide}/exports", Draft);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var export = await SettledAsync((await response.Content.ReadFromJsonAsync<ExportDto>())!.Id);

        export.Status.ShouldBe("failed");
        export.Error.ShouldNotBeNull().ShouldContain("bleed");
    }

    [Test, Order(5)]
    public async Task Source_WhenQueuedFromSettings_ShouldBecomeAZipOfMarkdownAndJson()
    {
        using var queue = await Client.PostAsync($"{Guide}/export-source", null);
        queue.EnsureSuccessStatusCode();
        var job = (await queue.Content.ReadFromJsonAsync<Codex.Contracts.Jobs.JobDto>())!;

        var export = await SettledAsync(job.Id);

        export.Status.ShouldBe("succeeded", export.Error);
        export.Kind.ShouldBe("source");
        await using var stream = await Client.GetStreamAsync(export.FileUrl);
        using var zip = new ZipArchive(stream);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.ShouldContain("knowledge-base.json");
        names.ShouldContain("references.json");
        names.ShouldContain("manifest.json");
        names.Count(n => n.StartsWith("manuscript/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)).ShouldBeGreaterThan(3);
        var job2 = (await Client.GetFromJsonAsync<Codex.Contracts.Jobs.JobDto>($"/api/jobs/{job.Id}"))!;
        (job2.State, job2.Percent).ShouldBe(("done", 100));
    }
}
