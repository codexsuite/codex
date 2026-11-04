using System.Text.Json;

namespace Codex.Contracts.Print;

// What to preview: Scope is "scene", "chapter" or "book" (ScopeId names the scene or chapter). Template, Trim and
// Options override the book's choice for this preview only; TemplateVersionId previews that version of the template
// (a newer one before applying it) instead of the pinned one.
public sealed record LayoutPreviewRequest(
    string Scope,
    Guid? ScopeId = null,
    string? Template = null,
    string? Trim = null,
    IReadOnlyDictionary<string, JsonElement>? Options = null,
    Guid? TemplateVersionId = null);
