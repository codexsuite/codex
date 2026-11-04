using System.Net;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;

namespace Codex.WebApi.Tests.Telemetry;

// The built-in metrics reach the app's own meter provider (TelemetryCapture.AddTo). Not parallel: the endpoint goes in
// through the process environment, which every host starting at the same time would see too.
[TestFixture]
[NonParallelizable]
public class TelemetryMetricsTests
{
    private ClonedDatabase _database = null!;

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

    [Test]
    public async Task Request_WhenServed_ShouldRecordHttpServerRequestDurationAndTheRuntimeAndNpgsqlMeters()
    {
        using var environment = new ProcessEnvironment().Set("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1");
        using var capture = new TelemetryCapture();
        await using var host = CodexHost.Start(_database.ConnectionString, capture.AddTo);
        using var client = host.CreateClient();

        (await client.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The instrument records just after the response goes out, so flush until it shows.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (!HasRequestDuration(capture) && await timer.WaitForNextTickAsync(timeout.Token))
        {
            capture.Flush(host.Services);
        }

        HasRequestDuration(capture).ShouldBeTrue();
        capture.Metrics.ShouldContain(m => m.MeterName == "System.Runtime");
        capture.Metrics.ShouldContain(m => m.MeterName == "Npgsql");
    }

    private static bool HasRequestDuration(TelemetryCapture capture)
        => capture.Metrics.Any(m => m.Name == "http.server.request.duration" && m.MeterName == "Microsoft.AspNetCore.Hosting");
}
