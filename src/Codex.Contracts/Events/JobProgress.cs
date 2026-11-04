namespace Codex.Contracts.Events;

// A background job (export, import, print) moved. State is queued, running, done or failed; Percent is 0 to 100.
public sealed record JobProgress(Guid BookId, Guid JobId, string Kind, int Percent, string State, string? Message)
    : BookEvent(BookId);
