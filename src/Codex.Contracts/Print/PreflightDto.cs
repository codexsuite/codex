namespace Codex.Contracts.Print;

// What Export for printer would find in the whole book as it is now, for the template, trim and quality asked for.
// Verdict is blocked (a Blocks item exists: a final export is refused, a draft is not) or ready. EstimatedMegabytes is the
// placed images' renditions plus a fixed overhead (the page estimate is the client's, from word counts).
public sealed record PreflightDto(
    string Verdict,
    IReadOnlyList<PreflightItemDto> Blocks,
    IReadOnlyList<PreflightItemDto> Warnings,
    IReadOnlyList<PreflightItemDto> Notes,
    string Template,
    string TemplateVersion,
    string Trim,
    int EstimatedMegabytes);
