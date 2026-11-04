using System.Diagnostics;

namespace Codex.Application.Telemetry;

// One background job in the print and asset workers (contract C4 of SHIP-257): a job.* span from the moment the worker
// has claimed a job to the moment it is done with it, and when it ends a codex.jobs increment and a codex.job_duration
// sample tagged with the job kind and the outcome ok or error. Polling that finds no job never starts one, so an idle
// loop records nothing. A job the host stopped mid-run (it goes back to the queue) calls Abandon: the span ends and no
// metric is recorded, because the job has not ended. Nothing about the job (ids, titles, errors) goes on the span.
public sealed class JobSpan : IDisposable
{
    public const string Ok = "ok";
    public const string Error = "error";

    private readonly string _job;
    private readonly Activity? _activity;
    private readonly long _started = Stopwatch.GetTimestamp();
    private string? _outcome = Ok;

    private JobSpan(string job, string spanName)
    {
        _job = job;
        _activity = CodexTelemetry.Source.StartActivity(spanName);
    }

    public static JobSpan Export() => new(CodexTelemetry.JobKinds.Export, CodexTelemetry.Spans.JobExport);

    public static JobSpan Proof() => new(CodexTelemetry.JobKinds.Proof, CodexTelemetry.Spans.JobProof);

    public static JobSpan AssetRendition() => new(CodexTelemetry.JobKinds.AssetRendition, CodexTelemetry.Spans.JobAssetRendition);

    // The job ended in failure (its row is settled as failed).
    public void Fail() => _outcome = Error;

    // The job did not end (the host is stopping and it goes back to the queue): record no outcome.
    public void Abandon() => _outcome = null;

    public void Dispose()
    {
        if (_outcome != null)
        {
            _activity?.SetTag(CodexTelemetry.SpanTags.Outcome, _outcome);
            if (_outcome == Error)
            {
                _activity?.SetStatus(ActivityStatusCode.Error);
            }
            var tags = new TagList
            {
                { CodexTelemetry.Tags.Job, _job },
                { CodexTelemetry.Tags.Outcome, _outcome },
            };
            CodexTelemetry.Jobs.Add(1, tags);
            CodexTelemetry.JobDuration.Record(Stopwatch.GetElapsedTime(_started).TotalSeconds, tags);
        }
        _activity?.Dispose();
    }
}
