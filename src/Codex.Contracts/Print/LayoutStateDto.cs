namespace Codex.Contracts.Print;

// GET /api/books/{id}/layout: every template the book can pick, and what it uses now (Template, Trim; each template's
// Options carry the book's stored values when it is the chosen one).
public sealed record LayoutStateDto(string Template, string Trim, IReadOnlyList<LayoutTemplateDto> Templates);
