using System.Diagnostics.Metrics;
using Codex.Application.Telemetry;
using Codex.TestSupport.Telemetry;
using OpenTelemetry.Metrics;

namespace Codex.UnitTest.Contracts;

// Pins Codex's telemetry surface to contract C4 of SHIP-257: dashboards, the agent runbook and the PromQL recipes are
// written against these exact names, units and tag keys, so changing one breaks them.
[TestFixture]
public class CodexTelemetryTests
{
    [Test]
    public void SourceAndMeter_WhenRead_ShouldBothBeNamedCodex()
    {
        CodexTelemetry.Source.Name.ShouldBe("Codex");
        CodexTelemetry.Meter.Name.ShouldBe("Codex");
        CodexTelemetry.SourceName.ShouldBe("Codex");
        CodexTelemetry.MeterName.ShouldBe("Codex");
    }

    [Test]
    public void McpToolCalls_WhenRead_ShouldBeACounterWithNoUnit()
    {
        var instrument = CodexTelemetry.McpToolCalls;

        instrument.ShouldBeOfType<Counter<long>>();
        instrument.Name.ShouldBe("codex.mcp.tool_calls");
        instrument.Unit.ShouldBeNull();
        instrument.Meter.ShouldBeSameAs(CodexTelemetry.Meter);
    }

    [Test]
    public void McpToolDuration_WhenRead_ShouldBeAHistogramInSeconds()
    {
        var instrument = CodexTelemetry.McpToolDuration;

        instrument.ShouldBeOfType<Histogram<double>>();
        instrument.Name.ShouldBe("codex.mcp.tool_duration");
        instrument.Unit.ShouldBe("s");
        instrument.Meter.ShouldBeSameAs(CodexTelemetry.Meter);
    }

    [Test]
    public void Jobs_WhenRead_ShouldBeACounterWithNoUnit()
    {
        var instrument = CodexTelemetry.Jobs;

        instrument.ShouldBeOfType<Counter<long>>();
        instrument.Name.ShouldBe("codex.jobs");
        instrument.Unit.ShouldBeNull();
        instrument.Meter.ShouldBeSameAs(CodexTelemetry.Meter);
    }

    [Test]
    public void JobDuration_WhenRead_ShouldBeAHistogramInSeconds()
    {
        var instrument = CodexTelemetry.JobDuration;

        instrument.ShouldBeOfType<Histogram<double>>();
        instrument.Name.ShouldBe("codex.job_duration");
        instrument.Unit.ShouldBe("s");
        instrument.Meter.ShouldBeSameAs(CodexTelemetry.Meter);
    }

    [Test]
    public void McpToolDuration_WhenExported_ShouldUseSecondScaleBucketBoundaries()
    {
        using var capture = TelemetryCapture.Start();

        CodexTelemetry.McpToolDuration.Record(0.1, new KeyValuePair<string, object?>(CodexTelemetry.Tags.Tool, "bucket_contract"));
        capture.Flush();

        BucketBoundsOf(capture, "codex.mcp.tool_duration", "bucket_contract")
            .ShouldBe([0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30]);
    }

    [Test]
    public void JobDuration_WhenExported_ShouldUseSecondScaleBucketBoundaries()
    {
        using var capture = TelemetryCapture.Start();

        CodexTelemetry.JobDuration.Record(2, new KeyValuePair<string, object?>(CodexTelemetry.Tags.Job, "bucket_contract"));
        capture.Flush();

        BucketBoundsOf(capture, "codex.job_duration", "bucket_contract")
            .ShouldBe([0.1, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600]);
    }

    // The finite upper bounds of the histogram series carrying this tag value (the last bucket is +Inf).
    private static double[] BucketBoundsOf(TelemetryCapture capture, string name, string tagValue)
    {
        var snapshot = capture.Metrics.Last(m => m.MeterName == "Codex" && m.Name == name);
        foreach (var point in snapshot.MetricPoints)
        {
            foreach (var tag in point.Tags)
            {
                if (tag.Value?.ToString() == tagValue)
                {
                    var bounds = new List<double>();
                    foreach (var bucket in point.GetHistogramBuckets())
                    {
                        bounds.Add(bucket.ExplicitBound);
                    }
                    return bounds.Where(double.IsFinite).ToArray();
                }
            }
        }
        throw new InvalidOperationException($"No {name} series tagged {tagValue}.");
    }

    [Test]
    public void TagKeysAndJobValues_WhenRead_ShouldMatchC4()
    {
        CodexTelemetry.Tags.Tool.ShouldBe("tool");
        CodexTelemetry.Tags.Outcome.ShouldBe("outcome");
        CodexTelemetry.Tags.Job.ShouldBe("job");
        CodexTelemetry.JobKinds.Export.ShouldBe("export");
        CodexTelemetry.JobKinds.Proof.ShouldBe("proof");
        CodexTelemetry.JobKinds.AssetRendition.ShouldBe("asset_rendition");
    }

    [Test]
    public void SpanNamesAndTags_WhenRead_ShouldMatchC4()
    {
        CodexTelemetry.Spans.McpToolCall.ShouldBe("mcp.tool_call");
        CodexTelemetry.Spans.JobExport.ShouldBe("job.export");
        CodexTelemetry.Spans.JobProof.ShouldBe("job.proof");
        CodexTelemetry.Spans.JobAssetRendition.ShouldBe("job.asset_rendition");
        CodexTelemetry.SpanTags.McpTool.ShouldBe("mcp.tool");
        CodexTelemetry.SpanTags.Outcome.ShouldBe("codex.outcome");
    }

    [Test]
    public void Capture_WhenCodexEmitsASpanAndAMetric_ShouldExportBoth()
    {
        using var capture = TelemetryCapture.Start();

        using (var activity = CodexTelemetry.Source.StartActivity(CodexTelemetry.Spans.JobProof))
        {
            activity.ShouldNotBeNull();
        }
        CodexTelemetry.Jobs.Add(1, new KeyValuePair<string, object?>(CodexTelemetry.Tags.Job, CodexTelemetry.JobKinds.Proof));
        capture.Flush();

        capture.Activities.ShouldContain(a => a.OperationName == "job.proof");
        capture.Metrics.ShouldContain(m => m.Name == "codex.jobs" && m.MeterName == "Codex");
    }
}
