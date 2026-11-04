using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codex.Application.Features.Print;
using Codex.Application.Features.Print.Export;
using Codex.Application.Features.Print.Proof;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Codex.WebApi.Tests.Telemetry;

// Each background job the print and asset workers run records one job.* span and the two job metrics (contract C4 of
// SHIP-257), and polling that finds nothing records nothing. Driven through the real host and its in-process workers: an
// upload (asset renditions), a source export, and a requeue of the seed figure whose file is missing (a failed job). The
// host starts with a dead OTLP endpoint (so the app registers its providers, as the box does) and the capture's
// in-memory exporters added. Not parallel: the endpoint is process environment, and the source and meter are process-wide.
[TestFixture]
[NonParallelizable]
[CancelAfter(240000)]
public class JobTelemetryTests
{
    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string DeadEndpoint = "http://127.0.0.1:1";
    private static readonly string Guide = $"/api/books/{SeedIds.Books.PlayersGuide}";

    private ClonedDatabase _database = null!;
    private string? _previousEndpoint;
    private TelemetryCapture _capture = null!;
    private CodexHost _host = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task CloneDatabaseAsync() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropDatabaseAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
    }

    [SetUp]
    public void StartHost()
    {
        _previousEndpoint = Environment.GetEnvironmentVariable(EndpointKey);
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        _capture = new TelemetryCapture();
        _host = CodexHost.Start(_database.ConnectionString, _capture.AddTo);
        _client = _host.CreateClient();
    }

    [TearDown]
    public async Task StopHostAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        _capture.Dispose();
        Environment.SetEnvironmentVariable(EndpointKey, _previousEndpoint);
    }

    [Test]
    public async Task AssetRendition_WhenAnUploadIsRendered_ShouldRecordOneSpanAndTheJobMetricsWithOutcomeOk()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Print.TestImages.Photo(120, 80));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(file, "file", "small.png");
        using var upload = await _client.PostAsync($"{Guide}/assets", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var asset = (await upload.Content.ReadFromJsonAsync<AssetDto>())!;

        (await JobPolling.AssetAsync(_client, asset.Id)).ShouldBe("ready");

        Recorded().ShouldBe(Expected("job.asset_rendition", "asset_rendition", "ok"));
    }

    [Test]
    public async Task AssetRendition_WhenTheOriginalFileIsMissing_ShouldRecordOutcomeError()
    {
        using var requeue = await _client.PostAsync($"/api/assets/{SeedIds.Assets.PlayersGuideFigure}/renditions", null);
        requeue.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await JobPolling.AssetAsync(_client, SeedIds.Assets.PlayersGuideFigure)).ShouldBe("failed");

        Recorded().ShouldBe(Expected("job.asset_rendition", "asset_rendition", "error"));
    }

    [Test]
    public async Task Export_WhenASourceExportIsBuilt_ShouldRecordOneSpanAndTheJobMetricsWithOutcomeOk()
    {
        using var queue = await _client.PostAsync($"{Guide}/export-source", null);
        queue.EnsureSuccessStatusCode();
        var job = (await queue.Content.ReadFromJsonAsync<Codex.Contracts.Jobs.JobDto>())!;

        (await JobPolling.ExportAsync(_client, job.Id)).ShouldBe("succeeded");

        Recorded().ShouldBe(Expected("job.export", "export", "ok"));
    }

    [Test]
    public async Task Export_WhenTheTemplateCannotTakeABleed_ShouldRecordOutcomeError()
    {
        using var chosen = await _client.PutAsJsonAsync($"{Guide}/layout", new LayoutSelectionRequest("manuscript"));
        chosen.EnsureSuccessStatusCode();
        using var response = await _client.PostAsJsonAsync(
            $"{Guide}/exports", new ExportRequest(Bleed: "3mm", CropMarks: true, Quality: "screen", Draft: true));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await response.Content.ReadFromJsonAsync<ExportDto>())!;

        (await JobPolling.ExportAsync(_client, queued.Id)).ShouldBe("failed");

        Recorded().ShouldBe(Expected("job.export", "export", "error"));
    }

    [Test]
    public async Task Workers_WhenNothingIsQueued_ShouldRecordNoSpanAndNoMetric()
    {
        var exported = await _host.Services.GetRequiredService<ExportWorker>().ProcessNextAsync(CancellationToken.None);
        var proved = await _host.Services.GetRequiredService<ProofWorker>().ProcessNextAsync(CancellationToken.None);
        var rendered = await _host.Services.GetRequiredService<AssetRenditionWorker>().ProcessNextAsync(CancellationToken.None);

        (exported, proved, rendered).ShouldBe((false, false, false));
        _capture.Flush(_host.Services);
        _capture.Activities.Where(a => a.OperationName.StartsWith("job.", StringComparison.Ordinal)).ShouldBeEmpty();
        _capture.Metrics.Where(m => m.Name is "codex.jobs" or "codex.job_duration").SelectMany(m => m.MetricPoints).ShouldBeEmpty();
    }

    // What one job left behind, as text: the single job.* span with its tags and status, and the single series of each job
    // metric with its tags and count. Fails when there is more than one span or series.
    private string Recorded()
    {
        _capture.Flush(_host.Services);
        var span = _capture.Activities.Where(a => a.Source.Name == "Codex" && a.OperationName.StartsWith("job.", StringComparison.Ordinal)).ShouldHaveSingleItem();
        var tags = string.Join(",", span.TagObjects.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
        return $"{span.OperationName} [{tags}] {span.Status} | jobs {Series("codex.jobs")} | duration {Series("codex.job_duration")}";
    }

    private static string Expected(string spanName, string job, string outcome) =>
        $"{spanName} [codex.outcome={outcome}] {(outcome == "ok" ? ActivityStatusCode.Unset : ActivityStatusCode.Error)} | jobs job={job},outcome={outcome}:1 | duration job={job},outcome={outcome}:1";

    // The last export of the instrument (its values are cumulative): each series' tags and its count, as text.
    private string Series(string name)
    {
        var last = _capture.Metrics.Last(m => m.MeterName == "Codex" && m.Name == name);
        var series = new List<string>();
        foreach (var point in last.MetricPoints)
        {
            var tags = new List<string>();
            foreach (var tag in point.Tags)
            {
                tags.Add($"{tag.Key}={tag.Value}");
            }
            var count = last.MetricType == MetricType.LongSum ? point.GetSumLong() : point.GetHistogramCount();
            series.Add($"{string.Join(",", tags.OrderBy(t => t, StringComparer.Ordinal))}:{count}");
        }
        return string.Join(";", series);
    }
}

// Waits for the in-process workers by polling the API, as AssetApiFixture and ExportApiFixture do.
internal static class JobPolling
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static async Task<string> AssetAsync(HttpClient client, Guid assetId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var asset = (await client.GetFromJsonAsync<AssetDto>($"/api/assets/{assetId}/info"))!;
            if (asset.Status != "processing")
            {
                return asset.Status;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the rendition worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }

    public static async Task<string> ExportAsync(HttpClient client, Guid exportId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var export = (await client.GetFromJsonAsync<ExportDto>($"/api/exports/{exportId}"))!;
            if (export.Status is "succeeded" or "failed" or "cancelled")
            {
                return export.Status;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the export worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    public static async Task<ProofDto> ProofAsync(HttpClient client, Guid proofId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var proof = (await client.GetFromJsonAsync<ProofDto>($"/api/proofs/{proofId}"))!;
            if (proof.Status is "succeeded" or "failed")
            {
                return proof;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the proof worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}
