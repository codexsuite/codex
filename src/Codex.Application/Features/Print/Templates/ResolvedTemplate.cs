namespace Codex.Application.Features.Print.Templates;

// The template version a render uses for a book, its package, and the trim and options (JSON object text) the book
// stored for that template (book_layout_template), if it has used it before.
public sealed record ResolvedTemplate(Guid TemplateId, Guid VersionId, TemplatePackage Package, string? StoredTrim, string? StoredOptions);
