using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codex.Application.Features.Print.Templates;

// A layout template package on disk, as TemplateLoader read it. Problems lists what stops it compiling (a missing
// required file, an unknown trim or engine); Notices what the UI should say without blocking, such as no preview.css.
public sealed record TemplatePackage(
    string Slug,
    string Name,
    string Version,
    string Engine,
    string Directory,
    IReadOnlyList<string> Trims,
    PageLayout Page,
    CitationForm Citations,
    bool AllowsRawLatex,
    IReadOnlyList<string> Filters,
    IReadOnlyDictionary<string, TemplateOption> Options,
    IReadOnlyDictionary<string, string> Directives,
    IReadOnlyList<string> Files,
    bool HasPreviewCss,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notices)
{
    public const string ManifestFile = "template.json";
    public const string WrapperFile = "wrapper.tex";
    public const string PreviewCssFile = "preview.css";

    // TeX support files (classes, styles, fonts, images) live here and are copied to the job folder's root, so a
    // wrapper's \documentclass{dndbook} finds them.
    public const string TexFolder = "tex";

    public const string NoPreviewCssNotice =
        "This template has no preview.css, so Quick preview uses the neutral house style. Render a proof to see the real layout.";

    public bool CanCompile => Problems.Count == 0;

    // SHA-256 over every file of the package (relative path and bytes, in path order): part of a render's content
    // hash, so editing the wrapper, a filter, a class or the manifest invalidates cached proofs.
    public string ContentHash { get; init; } = string.Empty;

    // The manifest's changelog: what this version changed, one line each.
    public IReadOnlyList<string> Changelog { get; init; } = [];

    public string ManifestPath => Path.Combine(Directory, ManifestFile);

    public static readonly JsonDocumentOptions ManifestReading = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // The manifest as layout_template_version.manifest stores it: template.json without comments or trailing commas.
    public string ManifestJson() => JsonNode.Parse(File.ReadAllText(ManifestPath), null, ManifestReading)!.ToJsonString();
}
