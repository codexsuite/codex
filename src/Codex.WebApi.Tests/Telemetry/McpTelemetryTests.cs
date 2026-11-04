using Codex.Application.Telemetry;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Codex.TestSupport.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codex.WebApi.Tests.Telemetry;

// Every MCP tool call on /mcp records one mcp.tool_call span and the two MCP metrics (contract C4 of SHIP-257), driven
// through a real MCP client against the real host. The host starts with a dead OTLP endpoint (so the app registers its
// providers, as the box does) and the capture's in-memory exporters added; one extra tool that throws stands in for a
// bug in a tool. Not parallel: the endpoint is process environment, and the source and meter are process-wide.
[TestFixture]
[NonParallelizable]
[CancelAfter(120000)]
public class McpTelemetryTests
{
    private const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string DeadEndpoint = "http://127.0.0.1:1";
    private const string RequestSource = "Microsoft.AspNetCore";

    // An id nothing has: the tool answers not_found. It must not reach the span or the tags.
    private const string MissingJobId = "5e1f0c9a-7d47-4b1e-9d8b-3a6f2b1c0d11";

    private ClonedDatabase _database = null!;
    private string? _previousEndpoint;
    private TelemetryCapture _capture = null!;
    private CodexHost _host = null!;
    private McpClient _client = null!;
    private HttpClient _http = null!;

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
    public async Task StartHostAsync()
    {
        _previousEndpoint = Environment.GetEnvironmentVariable(EndpointKey);
        Environment.SetEnvironmentVariable(EndpointKey, DeadEndpoint);
        _capture = new TelemetryCapture();
        _host = CodexHost.Start(_database.ConnectionString, services =>
        {
            _capture.AddTo(services);
            // ASP.NET Core's own source, so the request span is recorded and sampled and the tool call becomes its child.
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(RequestSource));
            services.AddMcpServer().WithTools([ThrowingTool()]);
        });
        // The client is in this process, so the app's HTTP client instrumentation would trace it as a root client span, which
        // the sampler drops, and its unsampled traceparent would drop the server's spans too. A real caller is another
        // process; sending no traceparent makes the request reach the server as it would there.
        _http = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null });
        _client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, _http, ownsHttpClient: true), cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [TearDown]
    public async Task StopHostAsync()
    {
        await _client.DisposeAsync();
        _http.Dispose();
        await _host.DisposeAsync();
        _capture.Dispose();
        Environment.SetEnvironmentVariable(EndpointKey, _previousEndpoint);
    }

    [Test]
    public async Task ToolCall_WhenItSucceeds_ShouldRecordOneSpanAndTheMetricsWithOutcomeOk()
    {
        var result = await _client.CallToolAsync("list_books", cancellationToken: TestContext.CurrentContext.CancellationToken);
        result.IsError.ShouldNotBe(true);

        _capture.Flush(_host.Services);
        var span = Spans().ShouldHaveSingleItem();
        TagsOf(span).ShouldBe(new Dictionary<string, string> { ["mcp.tool"] = "list_books", ["codex.outcome"] = "ok" });
        span.Status.ShouldBe(System.Diagnostics.ActivityStatusCode.Unset);
        // The request span ends after the response is sent, so read it through the tool span's parent, not the export.
        var request = span.Parent.ShouldNotBeNull();
        (request.Source.Name, request.OperationName).ShouldBe((RequestSource, "Microsoft.AspNetCore.Hosting.HttpRequestIn"));
        var tags = new Dictionary<string, string> { ["tool"] = "list_books", ["outcome"] = "ok" };
        Count("codex.mcp.tool_calls", tags).ShouldBe(1);
        Count("codex.mcp.tool_duration", tags).ShouldBe(1);
    }

    [Test]
    public async Task ToolCall_WhenItReturnsAnErrorResult_ShouldRecordOutcomeError()
    {
        var result = await _client.CallToolAsync(
            "get_job", new Dictionary<string, object?> { ["job_id"] = MissingJobId }, cancellationToken: TestContext.CurrentContext.CancellationToken);
        result.IsError.ShouldBe(true);

        _capture.Flush(_host.Services);
        var span = Spans().ShouldHaveSingleItem();
        TagsOf(span).ShouldBe(new Dictionary<string, string> { ["mcp.tool"] = "get_job", ["codex.outcome"] = "error" });
        span.Status.ShouldBe(System.Diagnostics.ActivityStatusCode.Error);
        var tags = new Dictionary<string, string> { ["tool"] = "get_job", ["outcome"] = "error" };
        Count("codex.mcp.tool_calls", tags).ShouldBe(1);
        Count("codex.mcp.tool_duration", tags).ShouldBe(1);
    }

    [Test]
    public async Task ToolCall_WhenTheToolThrows_ShouldRecordOutcomeException()
    {
        var result = await _client.CallToolAsync(
            "telemetry_probe_throw", new Dictionary<string, object?> { ["note"] = "private prose" }, cancellationToken: TestContext.CurrentContext.CancellationToken);
        result.IsError.ShouldBe(true);

        _capture.Flush(_host.Services);
        var span = Spans().ShouldHaveSingleItem();
        TagsOf(span).ShouldBe(new Dictionary<string, string> { ["mcp.tool"] = "telemetry_probe_throw", ["codex.outcome"] = "exception" });
        span.Status.ShouldBe(System.Diagnostics.ActivityStatusCode.Error);
        var tags = new Dictionary<string, string> { ["tool"] = "telemetry_probe_throw", ["outcome"] = "exception" };
        Count("codex.mcp.tool_calls", tags).ShouldBe(1);
        Count("codex.mcp.tool_duration", tags).ShouldBe(1);
    }

    [Test]
    public async Task ToolCall_WhenTheNameIsNotATool_ShouldRecordUnknownRatherThanTheClientsName()
    {
        var result = await _client.CallToolAsync("not_a_tool_abc123", cancellationToken: TestContext.CurrentContext.CancellationToken);
        result.IsError.ShouldBe(true);

        _capture.Flush(_host.Services);
        var span = Spans().ShouldHaveSingleItem();
        TagsOf(span).ShouldBe(new Dictionary<string, string> { ["mcp.tool"] = "unknown", ["codex.outcome"] = "error" });
    }

    [Test]
    public async Task ToolCall_WithArguments_ShouldPutNoArgumentValueOnTheSpanOrTheTags()
    {
        await _client.CallToolAsync(
            "get_job", new Dictionary<string, object?> { ["job_id"] = MissingJobId }, cancellationToken: TestContext.CurrentContext.CancellationToken);
        await _client.CallToolAsync(
            "telemetry_probe_throw", new Dictionary<string, object?> { ["note"] = "private prose" }, cancellationToken: TestContext.CurrentContext.CancellationToken);

        _capture.Flush(_host.Services);
        var spans = Spans();
        spans.Count.ShouldBe(2);
        foreach (var span in spans)
        {
            TagsOf(span).Keys.OrderBy(k => k).ShouldBe(["codex.outcome", "mcp.tool"]);
            span.Events.ShouldBeEmpty();
            span.StatusDescription.ShouldBeNull();
        }
        var values = spans.SelectMany(s => TagsOf(s).Values).Concat(MetricTagValues()).ToList();
        values.ShouldNotContain(v => v.Contains(MissingJobId, StringComparison.OrdinalIgnoreCase) || v.Contains("private prose", StringComparison.Ordinal));
        MetricTagKeys().Distinct().OrderBy(k => k).ShouldBe(["outcome", "tool"]);
    }

    private List<System.Diagnostics.Activity> Spans() =>
        _capture.Activities.Where(a => a.Source.Name == "Codex" && a.OperationName == "mcp.tool_call").ToList();

    private static Dictionary<string, string> TagsOf(System.Diagnostics.Activity span) =>
        span.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString() ?? "");

    private IEnumerable<MetricSnapshot> Snapshots(string name) =>
        _capture.Metrics.Where(m => m.MeterName == "Codex" && m.Name == name);

    // The count of the series with exactly these tags, from the last export (the values are cumulative).
    private long Count(string name, Dictionary<string, string> tags)
    {
        var last = Snapshots(name).Last();
        foreach (var point in last.MetricPoints)
        {
            if (Tags(point).SequenceEqual(tags.OrderBy(t => t.Key)))
            {
                return last.MetricType == MetricType.LongSum ? point.GetSumLong() : point.GetHistogramCount();
            }
        }
        return 0;
    }

    private IEnumerable<string> MetricTagKeys() => AllPoints().SelectMany(p => Tags(p).Select(t => t.Key));

    private IEnumerable<string> MetricTagValues() => AllPoints().SelectMany(p => Tags(p).Select(t => t.Value));

    private IEnumerable<MetricPoint> AllPoints() =>
        _capture.Metrics.Where(m => m.MeterName == "Codex" && m.Name.StartsWith("codex.mcp.", StringComparison.Ordinal)).SelectMany(m => m.MetricPoints);

    private static IEnumerable<KeyValuePair<string, string>> Tags(MetricPoint point)
    {
        var tags = new List<KeyValuePair<string, string>>();
        foreach (var tag in point.Tags)
        {
            tags.Add(new KeyValuePair<string, string>(tag.Key, tag.Value?.ToString() ?? ""));
        }
        return tags.OrderBy(t => t.Key);
    }

    // A tool with a bug in it: the filter must see the exception that McpToolErrors turns into internal_error.
    private static McpServerTool ThrowingTool() => McpServerTool.Create(
        (string note) => Boom(note), new McpServerToolCreateOptions { Name = "telemetry_probe_throw" });

    private static string Boom(string note) => throw new InvalidOperationException($"boom {note}");
}
