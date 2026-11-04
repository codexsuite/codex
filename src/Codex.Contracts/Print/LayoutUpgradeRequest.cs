namespace Codex.Contracts.Print;

// POST /api/books/{id}/layout/upgrade: re-pins the book's current template to TemplateVersionId (a published version of
// that template), or to its newest published version when null.
public sealed record LayoutUpgradeRequest(Guid? TemplateVersionId = null);
