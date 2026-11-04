namespace Codex.Contracts.Print;

// A proof job (export_job kind proof). Status is queued, running, succeeded, failed or cancelled; Step and Progress
// (0 to 1) describe a running one. Cached is true on the answer to a request that reused a finished or running proof
// instead of queuing a new one. PdfUrl is set once it succeeded. Log (pandoc's then Tectonic's output) is filled by
// GET /api/proofs/{id} only, and is what a failed compile shows; Error is the one-line reason.
public sealed record ProofDto(
    Guid Id,
    Guid ChapterId,
    string Status,
    string? Step,
    double? Progress,
    string CacheKey,
    string TemplateSlug,
    string TemplateVersion,
    string Trim,
    int? PageCount,
    string? Error,
    string? Log,
    bool Cached,
    string? PdfUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);
