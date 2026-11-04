namespace Codex.Contracts.Jobs;

// A background job. Kind is the stored kind (proof, print, source). State is queued, running, done or failed
// (a cancelled job reads as failed). Percent is 0 to 100. ResultUrl is set once a job is done and has output.
public sealed record JobDto(
    Guid Id,
    Guid BookId,
    string Kind,
    string State,
    int Percent,
    string? Message,
    string? ResultUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
