namespace Codex.Contracts.Print;

// An export job (export_job kind print or source). Status is queued, running, succeeded, failed or cancelled; Step and
// Progress (0 to 1) describe a running one. Template, TemplateVersion, Trim, Bleed, CropMarks, Quality and Draft are the
// settings it was queued with (null for a source export). FileUrl is set once it succeeded. Log is filled by
// GET /api/exports/{id} only; Error is the one-line reason a failed job failed. Blocked and Warnings count the preflight
// snapshot taken when it was queued; Overfull lists the overfull-box lines found in the compile log. A manuscript export
// (kind docx, markdown or epub) has no template; Filename is its file's name and Notes what it left out ("1 raw-LaTeX block skipped").
public sealed record ExportDto(
    Guid Id,
    string Kind,
    string Status,
    string? Step,
    double? Progress,
    string? Template,
    string? TemplateVersion,
    string? Trim,
    string? Bleed,
    bool CropMarks,
    string? Quality,
    bool Draft,
    int? PageCount,
    long? Bytes,
    string? Error,
    string? Log,
    int Blocked,
    int Warnings,
    IReadOnlyList<string> Overfull,
    string? FileUrl,
    DateTimeOffset CreatedAt,
    string CreatedLabel,
    DateTimeOffset? FinishedAt,
    string? Filename = null,
    IReadOnlyList<string>? Notes = null);
