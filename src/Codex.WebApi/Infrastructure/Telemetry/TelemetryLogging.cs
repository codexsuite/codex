using OpenTelemetry;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace Codex.WebApi.Infrastructure.Telemetry;

// Adds the OTLP log sink beside the console and Seq sinks, only when OTEL_EXPORTER_OTLP_ENDPOINT is set. Endpoint,
// protocol and headers come from the same standard OTEL_* settings the trace and metric exporter reads, and the
// resource is the one they carry. Serilog stamps each record with Activity.Current's trace and span ids, which the sink
// sends as the log record's own ids. The sink batches and sends off the calling thread, so a slow or dead collector
// never holds up a request.
public static class TelemetryLogging
{
    public const string ProtocolKey = "OTEL_EXPORTER_OTLP_PROTOCOL";
    public const string HeadersKey = "OTEL_EXPORTER_OTLP_HEADERS";
    private const string HttpProtobuf = "http/protobuf";
    private const string LogsPath = "v1/logs";
    private const string TimeoutKey = "OTEL_EXPORTER_OTLP_TIMEOUT";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static LoggerConfiguration AddOtlpSink(this LoggerConfiguration logger, IConfiguration configuration)
    {
        var endpoint = configuration[TelemetryResource.EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return logger;
        }

        var http = string.Equals(configuration[ProtocolKey]?.Trim(), HttpProtobuf, StringComparison.OrdinalIgnoreCase);
        var timeout = int.TryParse(configuration[TimeoutKey], out var milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : DefaultTimeout;
        var headers = ParseHeaders(configuration[HeadersKey]);
        var attributes = TelemetryResource.Configure(ResourceBuilder.CreateEmpty(), configuration).Build().Attributes;

        return logger.WriteTo.OpenTelemetry(options =>
        {
            // Over HTTP the exporter appends the signal path to the base endpoint; the log sink wants it spelled out.
            // Only the logs endpoint is set, so the sink never posts spans (the OTel tracer provider owns those).
            options.Endpoint = null;
            options.LogsEndpoint = http ? $"{endpoint.TrimEnd('/')}/{LogsPath}" : endpoint;
            options.Protocol = http ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;

            // Without these the sink sends one record per POST. A batch of up to 100 goes every 2 seconds, and at most
            // 10000 records wait in memory while the collector is down; past that new records are dropped.
            options.BatchingOptions.BatchSizeLimit = 100;
            options.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
            options.BatchingOptions.QueueLimit = 10_000;

            // The sink's own POSTs go through HttpClient: keep the HTTP instrumentation from tracing them.
            options.OnBeginSuppressInstrumentation = SuppressInstrumentationScope.Begin;

            // The sink's HttpClient waits 100 seconds for an answer, which would hold the shutdown flush that long for a
            // collector that accepts connections and never replies. Same limit as the trace and metric exporters:
            // OTEL_EXPORTER_OTLP_TIMEOUT in milliseconds, 10 seconds by default.
            options.HttpMessageHandler = new TimeoutHandler(new SocketsHttpHandler(), timeout);
            foreach (var header in headers)
            {
                options.Headers[header.Key] = header.Value;
            }

            foreach (var attribute in attributes)
            {
                options.ResourceAttributes[attribute.Key] = attribute.Value;
            }
        });
    }

    // "key1=value1,key2=value2", as OTEL_EXPORTER_OTLP_HEADERS spells it.
    private static IEnumerable<KeyValuePair<string, string>> ParseHeaders(string? value)
    {
        foreach (var pair in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            if (index > 0)
            {
                yield return new(pair[..index].Trim(), Uri.UnescapeDataString(pair[(index + 1)..].Trim()));
            }
        }
    }

    private sealed class TimeoutHandler(HttpMessageHandler inner, TimeSpan timeout) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            return await base.SendAsync(request, limit.Token);
        }
    }
}
