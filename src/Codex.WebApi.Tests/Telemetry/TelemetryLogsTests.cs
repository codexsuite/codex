using System.Net.Http.Json;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Codex.WebApi.Tests.Telemetry;

// The Serilog OTLP sink: where it posts, what it carries, and that a log written during a request carries the request's
// trace and span ids. The collector is an in-process receiver. Not parallel: the endpoint goes in through the process
// environment, which every host starting at the same time would see too.
[TestFixture]
[NonParallelizable]
public class TelemetryLogsTests
{
    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ProtocolKey = "OTEL_EXPORTER_OTLP_PROTOCOL";
    private const string HeadersKey = "OTEL_EXPORTER_OTLP_HEADERS";
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";

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
    public async Task Log_WhenWrittenInsideARequest_ShouldReachTheCollectorWithTheRequestsTraceAndSpanId()
    {
        await using var receiver = await OtlpReceiver.StartAsync();
        using var environment = new ProcessEnvironment()
            .Set(EndpointKey, receiver.Address).Set(ProtocolKey, "http/protobuf").Set(HeadersKey, null).Set(ServiceNameKey, null);

        LogProbeTestController.LogProbeResult probe;
        await using (var host = CodexHost.Start(_database.ConnectionString, services => services.AddControllers().AddApplicationPart(typeof(TelemetryLogsTests).Assembly)))
        {
            using var client = host.CreateClient();
            probe = (await client.GetFromJsonAsync<LogProbeTestController.LogProbeResult>("/api/test-controller/log-probe"))!;
            probe.TraceId.ShouldNotBe("none");
        }

        // The OTLP log record carries the ids as raw bytes (16 and 8).
        var traceId = Convert.FromHexString(probe.TraceId);
        var spanId = Convert.FromHexString(probe.SpanId);
        var post = await receiver.WaitForPostAsync("logs", p => p.Contains(traceId) && p.Contains("probe inside request"u8));
        post.ShouldNotBeNull();
        post.Contains(spanId).ShouldBeTrue();
    }

    [Test]
    public async Task AddOtlpSink_WhenEndpointSet_ShouldPostLogsToTheLogsPathWithHeadersAndResource()
    {
        await using var receiver = await OtlpReceiver.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [EndpointKey] = receiver.Address,
            [ProtocolKey] = "http/protobuf",
            [HeadersKey] = "authorization=Bearer%20collector-token,x-other=1",
            [ServiceNameKey] = "codex-worker",
        }).Build();

        using (var logger = new LoggerConfiguration().AddOtlpSink(configuration).CreateLogger())
        {
            logger.Warning("somebody home");
        }

        var post = await receiver.WaitForPostAsync("logs", p => p.Contains("somebody home"u8));
        post.ShouldNotBeNull();
        post.Headers["authorization"].ShouldBe("Bearer collector-token");
        post.Headers["x-other"].ShouldBe("1");
        post.Contains("codex-worker"u8).ShouldBeTrue();
        post.Contains("service.instance.id"u8).ShouldBeTrue();
        receiver.PostsTo("traces").ShouldBeEmpty();
    }

    [Test]
    public async Task AddOtlpSink_WhenEndpointUnset_ShouldAddNoSink()
    {
        await using var receiver = await OtlpReceiver.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProtocolKey] = "http/protobuf",
            [HeadersKey] = "x-key=abc",
        }).Build();

        using (var logger = new LoggerConfiguration().AddOtlpSink(configuration).CreateLogger())
        {
            logger.Warning("nothing listens");
        }

        receiver.PostsTo("logs").ShouldBeEmpty();
    }
}
