using Codex.Application.Features.Print.Templates;

namespace Codex.Application.Features.Print.Render;

// An HTML render (Quick preview): Html is a pandoc html5 fragment whose images point at the asset rendition URLs;
// PreviewCss is the template's preview.css, or null when it has none (then the house style applies and
// Document's template notice says so). Template is the package it was rendered with.
public sealed record RenderedHtml(AssembledDocument Document, string Html, string? PreviewCss)
{
    public required TemplatePackage Template { get; init; }
}
