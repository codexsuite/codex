namespace Codex.Contracts.Print;

// GET /api/layout-template-versions/{id}/sample: the package's sample.md as a pandoc html5 fragment (through the
// template's Lua filters) and its preview.css (null without one), for the picker's thumbnail. WidthIn and HeightIn are the
// first trim's page, the margins the manifest's (inches), so the browser can draw the first page to scale.
public sealed record LayoutTemplateSampleDto(
    string Html,
    string? Css,
    string Trim,
    double WidthIn,
    double HeightIn,
    double MarginTopIn,
    double MarginBottomIn,
    double MarginInnerIn,
    double MarginOuterIn,
    int Columns);
