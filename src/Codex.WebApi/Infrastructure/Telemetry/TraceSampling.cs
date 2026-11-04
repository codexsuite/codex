using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Codex.WebApi.Infrastructure.Telemetry;

// Parent-based, always on: a span follows its parent's decision, and a root span is kept. The one exception is a root
// client span (Npgsql or HttpClient) with no trace around it. Those come from background loops, not from a request or a
// job: the print workers and the asset worker poll the database every 2 to 5 seconds, the book-event listener keeps its
// own connection, and startup migrates. Kept, they would be a trace of one polling query every couple of seconds for
// as long as the process runs. Database and HTTP work done inside a request or a job span is a child and is kept.
public sealed class TraceSampling : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => samplingParameters.Kind == ActivityKind.Client && samplingParameters.ParentContext.TraceId == default
            ? new SamplingResult(SamplingDecision.Drop)
            : _inner.ShouldSample(in samplingParameters);
}
