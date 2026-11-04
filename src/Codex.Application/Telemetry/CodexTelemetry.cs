using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Codex.Application.Telemetry;

// Codex's own telemetry surface (contract C4 of SHIP-257): one ActivitySource and one Meter, both named "Codex", and
// every instrument, span name, tag key and job value. This is the only place those names exist. Nothing listens unless
// OTEL_EXPORTER_OTLP_ENDPOINT is set (WebApi's TelemetryServices adds this source and meter to the providers).
// Tags stay bounded: a registered tool name, a job kind, an outcome. Never a book, scene or user id, a title or prose.
// The MCP tool filter (McpTelemetryFilter) records the tool calls; the export, proof and asset rendition workers record
// the jobs (JobSpan).
public static class CodexTelemetry
{
    public const string SourceName = "Codex";
    public const string MeterName = "Codex";

    public static readonly ActivitySource Source = new(SourceName);

    public static readonly Meter Meter = new(MeterName);

    // MCP tool calls, tagged Tags.Tool and Tags.Outcome.
    public static readonly Counter<long> McpToolCalls = Meter.CreateCounter<long>(
        "codex.mcp.tool_calls", description: "MCP tool calls, by tool and outcome.");

    // Bucket boundaries for the duration histograms, in seconds. The OpenTelemetry SDK's default boundaries (0, 5, 10, 25,
    // ... 10000) are millisecond-scale, so every call under 5 s fell in the first bucket and p95 read ~4.75 s for 0.1 s
    // calls. The advice travels with the instrument, so any exporter picks it up. The tool bounds follow the ones
    // ASP.NET Core uses for http.server.request.duration, with a longer tail.
    public static readonly double[] McpToolDurationBuckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];

    // Jobs (export, proof, asset rendition) take seconds to minutes.
    public static readonly double[] JobDurationBuckets = [0.1, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600];

    // How long an MCP tool call took, in seconds, tagged Tags.Tool and Tags.Outcome.
    public static readonly Histogram<double> McpToolDuration = Meter.CreateHistogram<double>(
        "codex.mcp.tool_duration", unit: "s", description: "How long an MCP tool call took.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = McpToolDurationBuckets });

    // Background jobs that ended, tagged Tags.Job (one of JobKinds) and Tags.Outcome.
    public static readonly Counter<long> Jobs = Meter.CreateCounter<long>(
        "codex.jobs", description: "Background jobs that ended, by job and outcome.");

    // How long a background job took, in seconds, tagged Tags.Job and Tags.Outcome.
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "codex.job_duration", unit: "s", description: "How long a background job took.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = JobDurationBuckets });

    // Metric tag keys.
    public static class Tags
    {
        public const string Tool = "tool";
        public const string Outcome = "outcome";
        public const string Job = "job";
    }

    // The values of Tags.Job.
    public static class JobKinds
    {
        public const string Export = "export";
        public const string Proof = "proof";
        public const string AssetRendition = "asset_rendition";
    }

    public static class Spans
    {
        public const string McpToolCall = "mcp.tool_call";
        public const string JobExport = "job.export";
        public const string JobProof = "job.proof";
        public const string JobAssetRendition = "job.asset_rendition";
    }

    // Span tag keys.
    public static class SpanTags
    {
        public const string McpTool = "mcp.tool";
        public const string Outcome = "codex.outcome";
    }
}
