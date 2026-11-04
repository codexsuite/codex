using System.Text.Json;

namespace Codex.Contracts.Print;

// POST /api/books/{id}/exports and /export/preflight: Export for printer. Template, Trim and Options override the book's
// Layout settings. Bleed is none, 3mm or 0.125in; CropMarks needs a bleed; Quality is print (300 dpi renditions) or
// screen (150 dpi proxies); Format is pdf. Draft exports despite blocking issues and prints DRAFT across every page.
public sealed record ExportRequest(
    string? Template = null,
    string? Trim = null,
    IReadOnlyDictionary<string, JsonElement>? Options = null,
    string Bleed = "none",
    bool CropMarks = false,
    string Quality = "print",
    string Format = "pdf",
    bool Draft = false);
