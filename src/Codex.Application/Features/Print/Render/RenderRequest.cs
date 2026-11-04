using System.Text.Json;

namespace Codex.Application.Features.Print.Render;

// One render. ScopeId names the scene or chapter (ignored for Book). Template, Trim and Options override the book's
// Layout settings (book_layout / book_layout_template); unset, the book's choice applies, then the template's first
// trim and option defaults. ImagePurpose picks the rendition placed: "print" (export), "proxy" (proof), "web" (HTML).
// TemplateVersionId renders that version (a built-in's or the book owner's template's) instead of the pinned one: a
// queued proof or export renders the version it recorded, and the upgrade preview the newer one.
public sealed record RenderRequest(
    Guid BookId,
    RenderScope Scope,
    Guid? ScopeId = null,
    string? Template = null,
    string? Trim = null,
    IReadOnlyDictionary<string, JsonElement>? Options = null,
    string ImagePurpose = "print",
    Guid? TemplateVersionId = null);
