namespace Codex.Contracts.Settings;

// One snapshot of a published book: a source export job (export_job kind source, settings.snapshot true), newest first.
// State is the job's status (queued, running, succeeded, failed, cancelled). Waiting is a queued snapshot that sits
// behind a running source export. FileUrl is the download once the zip is built; WhenLabel is the server-formatted day.
public sealed record SnapshotDto(
    Guid Id,
    string Label,
    string WhenLabel,
    int Words,
    string State,
    bool Waiting,
    string? FileUrl);
