using System.Text.Json;

namespace Codex.Contracts.Print;

// Quick preview: Html is a pandoc html5 fragment of the scope (each scene opens with an empty div.codex-scene
// carrying data-scene, data-chapter and data-label), Css the template's preview.css (null when it has none, then
// the house style applies and Notices say so). Figures cover every figure of the book; Flags the issues in scope.
// TemplateVersionId is the layout_template_version rendered.
public sealed record LayoutPreviewDto(
    string Html,
    string? Css,
    string Template,
    string TemplateName,
    string TemplateVersion,
    string Trim,
    IReadOnlyDictionary<string, JsonElement> Options,
    IReadOnlyList<string> Notices,
    IReadOnlyList<LayoutFlagDto> Flags,
    IReadOnlyList<LayoutFigureDto> Figures,
    string ContentHash,
    DateTimeOffset RenderedAt,
    Guid? TemplateVersionId = null);
