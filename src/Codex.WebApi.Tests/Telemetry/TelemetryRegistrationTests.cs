using System.Net;
using System.Net.Http.Json;
using Codex.Application.Telemetry;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;
using Codex.WebApi.Tests.Dtos;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codex.WebApi.Tests.Telemetry;

// Telemetry is off unless OTEL_EXPORTER_OTLP_ENDPOINT is set (contract C4), and the resource's service.name comes from
// OTEL_SERVICE_NAME (contract C1). The module reads the endpoint while Program.cs runs, before in-memory test settings
// are added, so both go in through the process environment, as the box compose sets them. Not parallel: every host
// starting at the same time would see them too. Each test starts its own host on the one database.
[TestFixture]
[NonParallelizable]
public class TelemetryRegistrationTests
{
    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";

    // Nothing listens there: export fails, and the app must not notice.
    private const string DeadEndpoint = "http://127.0.0.1:1";

    private ClonedDatabase _database = null!;
    private string? _previousEndpoint;
    private string? _previousServiceName;

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
    public void SaveEnvironment()
    {
        _previousEndpoint = Environment.GetEnvironmentVariable(EndpointKey);
        _previousServiceName = Environment.GetEnvironmentVariable(ServiceNameKey);
    }

    [TearDown]
    public void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(EndpointKey, _previousEndpoint);
        Environment.SetEnvironmentVariable(ServiceNameKey, _previousServiceName);
    }

    [Test]
    public async Task Host_WhenNoOtlpEndpoint_ShouldRegisterNoProviders()
    {
        Environment.SetEnvironmentVariable(EndpointKey, null);
        Environment.SetEnvironmentVariable(ServiceNameKey, null);

        await using var host = CodexHost.Start(_database.ConnectionString);

        host.Services.GetService<TracerProvider>().ShouldBeNull();
        host.Services.GetService<MeterProvider>().ShouldBeNull();
    }

    [Test]
    public async Task Host_WhenOtlpEndpointIsSetButDead_ShouldRegisterProvidersAsCodexAndServeHealth()
    {
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        Environment.SetEnvironmentVariable(ServiceNameKey, null);

        await using var host = CodexHost.Start(_database.ConnectionString);
        using var client = host.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tracerProvider = host.Services.GetService<TracerProvider>();
        tracerProvider.ShouldNotBeNull();
        host.Services.GetService<MeterProvider>().ShouldNotBeNull();
        var resource = tracerProvider.GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);
        resource["service.name"].ShouldBe("codex");
        var health = await response.Content.ReadFromJsonAsync<HealthDto>();
        resource["service.version"].ShouldBe(health!.Version);
        resource["service.instance.id"].ShouldNotBeNull();
    }

    [Test]
    public async Task Host_WhenServiceNameIsSetForTheWorker_ShouldUseIt()
    {
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        Environment.SetEnvironmentVariable(ServiceNameKey, "codex-worker");

        await using var host = CodexHost.Start(_database.ConnectionString);

        var resource = host.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);
        resource["service.name"].ShouldBe("codex-worker");
    }

    [Test]
    public async Task Providers_WhenCodexEmitsASpanAndAMetric_ShouldCarryBoth()
    {
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        Environment.SetEnvironmentVariable(ServiceNameKey, null);
        using var capture = new TelemetryCapture();

        await using var host = CodexHost.Start(_database.ConnectionString, capture.AddTo);
        using (CodexTelemetry.Source.StartActivity(CodexTelemetry.Spans.McpToolCall))
        {
            CodexTelemetry.McpToolCalls.Add(1, new KeyValuePair<string, object?>(CodexTelemetry.Tags.Tool, "list_books"));
        }
        capture.Flush(host.Services);

        capture.Activities.ShouldContain(a => a.OperationName == "mcp.tool_call" && a.Source.Name == "Codex");
        capture.Metrics.ShouldContain(m => m.Name == "codex.mcp.tool_calls" && m.MeterName == "Codex");
    }
}
