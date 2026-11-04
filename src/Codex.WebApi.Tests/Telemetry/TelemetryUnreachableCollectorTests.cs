using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Telemetry;

// Telemetry never touches a request. The collector here accepts connections and never answers, the worst a collector
// can do short of refusing: an exporter on the request path would hold every request for its full timeout. Not
// parallel: the endpoint goes in through the process environment, which every host starting at the same time would see too.
[TestFixture]
[NonParallelizable]
public class TelemetryUnreachableCollectorTests
{
    private const int Requests = 10;

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
    public async Task Requests_WhenTheCollectorNeverAnswers_ShouldStartAndServeAtTheirUsualSpeedWithNoErrors()
    {
        using var collector = new TcpListener(IPAddress.Loopback, 0);
        collector.Start();
        var accepted = new List<TcpClient>();
        using var stop = new CancellationTokenSource();
        var accepting = AcceptAndStayQuietAsync(collector, accepted, stop.Token);
        using var environment = new ProcessEnvironment()
            .Set("OTEL_EXPORTER_OTLP_ENDPOINT", $"http://127.0.0.1:{((IPEndPoint)collector.LocalEndpoint).Port}")
            .Set("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
            .Set("OTEL_EXPORTER_OTLP_TIMEOUT", "1000")
            .Set("OTEL_SERVICE_NAME", null);

        await using (var host = CodexHost.Start(_database.ConnectionString, services => services.AddControllers().AddApplicationPart(typeof(TelemetryUnreachableCollectorTests).Assembly)))
        {
            using var client = host.CreateClient();
            (await client.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Each request makes a server span, Npgsql spans, a metric and a log through the sink, all bound for the collector.
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < Requests; i++)
            {
                (await client.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
                (await client.GetFromJsonAsync<LogProbeTestController.LogProbeResult>("/api/test-controller/log-probe")).ShouldNotBeNull();
            }

            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }

        await stop.CancelAsync();
        await accepting;
        accepted.ForEach(c => c.Dispose());
    }

    private static async Task AcceptAndStayQuietAsync(TcpListener listener, List<TcpClient> accepted, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                accepted.Add(await listener.AcceptTcpClientAsync(stop));
            }
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
        }
    }
}
