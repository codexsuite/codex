using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Codex.WebApi.Tests.Telemetry;

// The proof worker records one job.proof span and the two job metrics per proof it settles (contract C4 of SHIP-257),
// like the export and asset workers (JobTelemetryTests). A real compile needs Tectonic and its TeX bundle, which not every
// host has, so the host runs the real queue, worker and assembler but swaps the render service's CompileAsync (the one
// step that runs TeX) for a double that either writes a small PDF or reports a TeX error. Everything else, the span, the
// tags, the metrics and the settled row, is the production path.
[TestFixture]
[NonParallelizable]
[CancelAfter(240000)]
public class ProofJobTelemetryTests
{
    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string DeadEndpoint = "http://127.0.0.1:1";
    private static readonly string Guide = $"/api/books/{SeedIds.Books.PlayersGuide}";

    private ClonedDatabase _database = null!;
    private string? _previousEndpoint;
    private TelemetryCapture _capture = null!;
    private CodexHost? _host;
    private HttpClient? _client;

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
    public void SetEndpoint()
    {
        _previousEndpoint = Environment.GetEnvironmentVariable(EndpointKey);
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        _capture = new TelemetryCapture();
    }

    [TearDown]
    public async Task StopHostAsync()
    {
        _client?.Dispose();
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
        _capture.Dispose();
        Environment.SetEnvironmentVariable(EndpointKey, _previousEndpoint);
    }

    [Test]
    public async Task Proof_WhenTheChapterCompiles_ShouldRecordOneSpanAndTheJobMetricsWithOutcomeOk()
    {
        StartHost(compileError: null);

        var proof = await ProofAsync();

        proof.Status.ShouldBe("succeeded", proof.Log);
        Recorded().ShouldBe(Expected("ok"));
    }

    [Test]
    public async Task Proof_WhenTheCompileFails_ShouldRecordOutcomeError()
    {
        StartHost(compileError: "TeX could not build the proof.");

        var proof = await ProofAsync();

        proof.Status.ShouldBe("failed");
        proof.Error.ShouldBe("TeX could not build the proof.");
        Recorded().ShouldBe(Expected("error"));
    }

    private void StartHost(string? compileError)
    {
        _host = CodexHost.Start(_database.ConnectionString, services =>
        {
            _capture.AddTo(services);
            var real = services.Single(d => d.ServiceType == typeof(IRenderService));
            services.Remove(real);
            services.AddSingleton<IRenderService>(provider => new CompileDouble(
                (IRenderService)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!), compileError));
        });
        _client = _host.CreateClient();
    }

    // Queues a proof of chapter 1 through the API (Rerender: the other test's succeeded proof is in the same database and would
    // otherwise answer from the cache) and waits for the in-process worker to settle it.
    private async Task<ProofDto> ProofAsync()
    {
        var client = _client!;
        using var chosen = await client.PutAsJsonAsync($"{Guide}/layout", new LayoutSelectionRequest("fantasy-rpg"));
        chosen.EnsureSuccessStatusCode();
        using var queued = await client.PostAsJsonAsync($"{Guide}/proofs", new ProofRequest(SeedIds.Chapters.PlayersGuideCh1, Rerender: true));
        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await queued.Content.ReadFromJsonAsync<ProofDto>())!.Id;

        return await JobPolling.ProofAsync(client, id);
    }

    // What the one proof left behind, as text: the single job.* span with its tags and status, and the single series of
    // each job metric with its tags and count. Fails when there is more than one span or series.
    private string Recorded()
    {
        _capture.Flush(_host!.Services);
        var span = _capture.Activities.Where(a => a.Source.Name == "Codex" && a.OperationName.StartsWith("job.", StringComparison.Ordinal)).ShouldHaveSingleItem();
        var tags = string.Join(",", span.TagObjects.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
        return $"{span.OperationName} [{tags}] {span.Status} | jobs {Series("codex.jobs")} | duration {Series("codex.job_duration")}";
    }

    private static string Expected(string outcome) =>
        $"job.proof [codex.outcome={outcome}] {(outcome == "ok" ? ActivityStatusCode.Unset : ActivityStatusCode.Error)} | jobs job=proof,outcome={outcome}:1 | duration job=proof,outcome={outcome}:1";

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

    // The real render service for everything but the TeX run: CompileAsync answers with a small PDF, or with a failed
    // result carrying the error (as the real one does for a TeX error), without starting pandoc or Tectonic.
    private sealed class CompileDouble(IRenderService inner, string? error) : IRenderService
    {
        public Task<(TemplatePackage Template, string Trim, IReadOnlyDictionary<string, System.Text.Json.JsonElement> Options)> ResolveAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.ResolveAsync(request, cancellationToken);

        public Task<ResolvedLayout> ResolveLayoutAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.ResolveLayoutAsync(request, cancellationToken);

        public Task<AssembledDocument> AssembleAsync(RenderRequest request, RenderTarget target, CancellationToken cancellationToken) =>
            inner.AssembleAsync(request, target, cancellationToken);

        public Task<RenderedPdf> RenderPdfAsync(RenderRequest request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken) =>
            inner.RenderPdfAsync(request, progress, cancellationToken);

        public Task<RenderedHtml> RenderHtmlAsync(RenderRequest request, CancellationToken cancellationToken) =>
            inner.RenderHtmlAsync(request, cancellationToken);

        public async Task<RenderedPdf> CompileAsync(AssembledDocument document, IProgress<RenderProgress>? progress, CancellationToken cancellationToken, PrintFinish? finish = null)
        {
            var folder = Directory.CreateTempSubdirectory("codex-proof-double-").FullName;
            if (error != null)
            {
                return new RenderedPdf(document, folder, null, "! Undefined control sequence.", error, TimeSpan.Zero);
            }
            var pdf = Path.Combine(folder, "document.pdf");
            await File.WriteAllTextAsync(pdf, "%PDF-1.4\n1 0 obj\n<< /Type /Pages /Count 1 >>\nendobj\n%%EOF\n", Encoding.Latin1, cancellationToken);
            return new RenderedPdf(document, folder, pdf, "compiled by the test double", null, TimeSpan.Zero);
        }
    }
}
