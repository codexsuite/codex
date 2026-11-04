using System.Text.Json;
using Codex.Application.Features.Print.Templates;

namespace Codex.Application.Features.Print.Render;

// What a render request resolves to: the template version (its package, layout_template and layout_template_version
// ids), the trim and every option's value.
public sealed record ResolvedLayout(
    TemplatePackage Template,
    Guid TemplateId,
    Guid TemplateVersionId,
    string Trim,
    IReadOnlyDictionary<string, JsonElement> Options);
