using System.Text.Json;

namespace Codex.Application.Features.Print.Templates;

// template.json, the package manifest (issue #13 names it template.yaml; no YAML reader ships with the app, so the
// manifest is JSON with the same fields). Read by TemplateLoader; layout_template_version.manifest stores the same
// object. Lengths are inches.
public sealed class TemplateManifest
{
    public string? Slug { get; set; }

    public string? Name { get; set; }

    public string? Version { get; set; }

    // Only "tectonic" compiles today (pdflatex, xelatex and lualatex are reserved by the schema).
    public string? Engine { get; set; }

    public IReadOnlyList<string>? Trims { get; init; }

    public ManifestPage? Page { get; set; }

    // "footnote" (a numbered note per source passage) or "author-date" ("(Beck 1999, p. 41)" after the passage).
    public string? Citations { get; set; }

    // Whether ```{=latex} blocks are kept. They are listed in the render's flags either way.
    public bool RawLatex { get; set; }

    // Lua filters run in order; each path is relative to the package.
    public IReadOnlyList<string>? Filters { get; init; }

    public IReadOnlyDictionary<string, TemplateOption>? Options { get; init; }

    // Directive -> how the template draws it, for the template picker and preflight; a directive missing here
    // falls back to the default rendering.
    public IReadOnlyDictionary<string, string>? Directives { get; init; }

    // Files of the package by path, as layout_template_version.manifest keeps them. wrapper.tex and every filter are
    // required; preview.css is optional (without it Quick preview uses the house style).
    public IReadOnlyDictionary<string, ManifestFile>? Files { get; init; }

    // What changed in this version, one line each: layout_template_version.changelog, shown with "v1.1.0 available".
    public IReadOnlyList<string>? Changelog { get; init; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
