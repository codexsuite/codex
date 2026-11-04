using System.Diagnostics;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Errors;
using Codex.Contracts.Storage;

namespace Codex.Application.Features.Print.Render;

// The render core every print surface calls: Quick preview (L7.3, RenderHtmlAsync), proofs (L7.4) and Export for
// printer (L7.5, RenderPdfAsync), and their preflight and cache keys (AssembleAsync). Only the worker image has
// pandoc, Tectonic and codex-sandbox; without them the tool runs answer 503 not_configured.
public interface IRenderService
{
    // The template, trim and options a request resolves to (request, then the book's Layout settings, then the
    // template's first trim and defaults). 404 unknown template; 400 a trim the template does not offer or an
    // option it does not declare or of the wrong type.
    Task<(TemplatePackage Template, string Trim, IReadOnlyDictionary<string, JsonElement> Options)> ResolveAsync(RenderRequest request, CancellationToken cancellationToken);

    // The same, with the template and version ids (ITemplateStore.ResolveAsync picks the version: the request's, else
    // the one the book pinned for that template, else the newest). The trim and options the book stored for that
    // template apply where the version still offers them.
    Task<ResolvedLayout> ResolveLayoutAsync(RenderRequest request, CancellationToken cancellationToken);

    // The pandoc AST, flags and content hash, without running any tool. 404 unknown book or scope.
    Task<AssembledDocument> AssembleAsync(RenderRequest request, RenderTarget target, CancellationToken cancellationToken);

    // A PDF of the scope with the template's wrapper and filters, compiled by Tectonic in a fresh job folder.
    // 400 when the template package has problems. A failed conversion or compile is a result with Error and Log,
    // not an exception. Dispose the result to delete its job folder.
    Task<RenderedPdf> RenderPdfAsync(RenderRequest request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken);

    // Compiles a document AssembleAsync (or ManuscriptAssembler) already built, with the template version it was
    // resolved to (a hand-assembled document: the built-in package it names): a proof job assembles first for its cache
    // key, then compiles only on a miss.
    // A PrintFinish (export only) adds bleed, crop marks or a DRAFT watermark to the template's page; 400 when the
    // template cannot take it.
    Task<RenderedPdf> CompileAsync(AssembledDocument document, IProgress<RenderProgress>? progress, CancellationToken cancellationToken, PrintFinish? finish = null);

    // An html5 fragment of the scope (images from the web rendition unless the request names another purpose).
    // A failed conversion is 500 with pandoc's message in the log.
    Task<RenderedHtml> RenderHtmlAsync(RenderRequest request, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class RenderService(
    IManuscriptReader reader,
    ITemplateLoader templates,
    ITemplateStore store,
    IPandocRunner pandoc,
    ITectonicRunner tectonic,
    IBlobStore blobs,
    RenderWorkFolders folders,
    ToolVersions tools) : IRenderService
{
    // A book that has not picked a template in Settings → Layout renders with this one.
    public const string DefaultTemplate = TemplateStore.DefaultTemplate;

    private const string TemplateFolder = "_template";
    private const string InputFile = "document.json";
    private const string TexFile = "main.tex";

    public async Task<(TemplatePackage Template, string Trim, IReadOnlyDictionary<string, JsonElement> Options)> ResolveAsync(
        RenderRequest request, CancellationToken cancellationToken)
    {
        var layout = await ResolveLayoutAsync(request, cancellationToken);
        return (layout.Template, layout.Trim, layout.Options);
    }

    public async Task<ResolvedLayout> ResolveLayoutAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        var resolved = await store.ResolveAsync(request.BookId, request.Template, request.TemplateVersionId, cancellationToken);
        var template = resolved.Package;
        var stored = resolved.StoredTrim is { } kept && template.Trims.Contains(kept, StringComparer.OrdinalIgnoreCase) ? kept : null;
        var trim = request.Trim ?? stored ?? template.Trims.FirstOrDefault()
            ?? throw ApiException.Validation($"The template {template.Name} offers no trim sizes.");
        if (!template.Trims.Contains(trim, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation($"The template {template.Name} offers the trims {string.Join(", ", template.Trims)}, not '{trim}'.");
        }

        var options = template.Options.ToDictionary(o => o.Key, o => o.Value.Default.Clone(), StringComparer.Ordinal);
        if (resolved.StoredOptions is { } storedOptions)
        {
            using var values = JsonDocument.Parse(storedOptions);
            foreach (var value in values.RootElement.EnumerateObject().Where(p => template.Options.TryGetValue(p.Name, out var o) && Fits(o, p.Value)))
            {
                options[value.Name] = value.Value.Clone();
            }
        }
        foreach (var (name, value) in request.Options ?? new Dictionary<string, JsonElement>())
        {
            if (!template.Options.TryGetValue(name, out var option))
            {
                throw ApiException.Validation($"The template {template.Name} has no option '{name}'.");
            }
            if (!Fits(option, value))
            {
                throw ApiException.Validation(option.Type == "choice"
                    ? $"The option '{name}' must be one of {string.Join(", ", option.Choices ?? [])}."
                    : $"The option '{name}' must be true or false.");
            }
            options[name] = value.Clone();
        }

        return new ResolvedLayout(template, resolved.TemplateId, resolved.VersionId, trim.ToLowerInvariant(), options);
    }

    public async Task<AssembledDocument> AssembleAsync(RenderRequest request, RenderTarget target, CancellationToken cancellationToken)
    {
        var (document, _) = await AssembleWithTemplateAsync(request, target, cancellationToken);
        return document;
    }

    public async Task<RenderedPdf> RenderPdfAsync(RenderRequest request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new RenderProgress("Assembling", 0.05));
        var (document, _) = await AssembleWithTemplateAsync(request, RenderTarget.Pdf, cancellationToken);
        return await CompileAsync(document, progress, cancellationToken);
    }

    public async Task<RenderedPdf> CompileAsync(AssembledDocument document, IProgress<RenderProgress>? progress, CancellationToken cancellationToken, PrintFinish? finish = null)
    {
        var clock = Stopwatch.StartNew();
        var template = document.TemplateVersionId == Guid.Empty
            ? templates.GetBuiltIn(document.TemplateSlug)
            : await store.OpenAsync(document.TemplateVersionId, cancellationToken);
        if (!template.CanCompile)
        {
            throw ApiException.Validation($"The template {template.Name} cannot compile: {string.Join(" ", template.Problems)}");
        }

        var job = folders.Create();
        try
        {
            var filters = await StageTemplateAsync(template, job, includeTex: true, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(job, InputFile), document.Ast.ToJsonString(), cancellationToken);

            progress?.Report(new RenderProgress("Placing images", 0.15));
            Directory.CreateDirectory(Path.Combine(job, "images"));
            foreach (var image in document.Images)
            {
                var blob = await blobs.GetAsync(image.Rendition.StorageKey, cancellationToken)
                    ?? throw new InvalidOperationException($"The image {image.Rendition.StorageKey} was removed during the render.");
                await using var content = blob.Content;
                await using var file = File.Create(Path.Combine(job, image.Source));
                await content.CopyToAsync(file, cancellationToken);
            }

            progress?.Report(new RenderProgress("Converting", 0.3));
            var converted = await pandoc.ConvertAsync(
                job, InputFile, "latex", Path.Combine(TemplateFolder, TemplatePackage.WrapperFile), filters, TexFile, cancellationToken);
            var pandocLog = string.Join("\n", new[] { converted.Stderr, converted.Stdout }.Where(s => s.Length > 0));
            if (converted.TimedOut || converted.ExitCode != 0)
            {
                return new RenderedPdf(document, job, null, pandocLog, ConversionError(converted), clock.Elapsed);
            }

            if (finish is { IsPlain: false })
            {
                var texPath = Path.Combine(job, TexFile);
                await File.WriteAllTextAsync(texPath, TexFinish.Apply(await File.ReadAllTextAsync(texPath, cancellationToken), finish, template.Name), cancellationToken);
            }

            progress?.Report(new RenderProgress("Typesetting", 0.4));
            var compiled = await tectonic.CompileAsync(job, TexFile, cancellationToken);
            progress?.Report(new RenderProgress(compiled.Succeeded ? "Done" : "Failed", 1));
            var log = string.Join("\n", new[] { pandocLog, compiled.Log }.Where(s => s.Length > 0));
            return new RenderedPdf(document, job, compiled.PdfPath, log, compiled.Error, clock.Elapsed);
        }
        catch
        {
            folders.TryDelete(job);
            throw;
        }
    }

    public async Task<RenderedHtml> RenderHtmlAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        var (document, template) = await AssembleWithTemplateAsync(
            request.ImagePurpose == "print" ? request with { ImagePurpose = "web" } : request, RenderTarget.Html, cancellationToken, sceneMarkers: true);
        var job = folders.Create();
        try
        {
            var filters = await StageTemplateAsync(template, job, includeTex: false, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(job, InputFile), document.Ast.ToJsonString(), cancellationToken);
            var converted = await pandoc.ConvertAsync(job, InputFile, "html5", null, filters, "document.html", cancellationToken);
            if (converted.TimedOut || converted.ExitCode != 0)
            {
                throw new InvalidOperationException($"{ConversionError(converted)} {converted.Stderr}");
            }

            var css = template.HasPreviewCss
                ? await File.ReadAllTextAsync(Path.Combine(template.Directory, TemplatePackage.PreviewCssFile), cancellationToken)
                : null;
            return new RenderedHtml(document, await File.ReadAllTextAsync(Path.Combine(job, "document.html"), cancellationToken), css) { Template = template };
        }
        finally
        {
            folders.TryDelete(job);
        }
    }

    private async Task<(AssembledDocument Document, TemplatePackage Template)> AssembleWithTemplateAsync(
        RenderRequest request, RenderTarget target, CancellationToken cancellationToken, bool sceneMarkers = false)
    {
        var layout = await ResolveLayoutAsync(request, cancellationToken);
        var snapshot = await reader.ReadAsync(request.BookId, request.Scope, request.ScopeId, request.ImagePurpose, cancellationToken);
        if (target == RenderTarget.Pdf)
        {
            // A rendition row whose file is gone is placed as missing rather than failing the compile.
            var present = new Dictionary<Guid, RenderImage>();
            foreach (var image in snapshot.Images.Values)
            {
                if (await blobs.ExistsAsync(image.StorageKey, cancellationToken))
                {
                    present[image.AssetId] = image;
                }
            }
            snapshot = snapshot with { Images = present };
        }

        var document = ManuscriptAssembler.Assemble(snapshot, layout.Template, layout.Trim, layout.Options, target, tools.Value, sceneMarkers);
        return (document with { TemplateVersionId = layout.TemplateVersionId }, layout.Template);
    }

    // The wrapper and filters go under _template/, the TeX support files (tex/) at the job root. Returns the filter
    // paths relative to the job folder.
    private static async Task<IReadOnlyList<string>> StageTemplateAsync(TemplatePackage template, string job, bool includeTex, CancellationToken cancellationToken)
    {
        var staged = Path.Combine(job, TemplateFolder);
        foreach (var file in template.Filters.Append(TemplatePackage.WrapperFile))
        {
            var target = Path.Combine(staged, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = File.OpenRead(Path.Combine(template.Directory, file));
            await using var copy = File.Create(target);
            await source.CopyToAsync(copy, cancellationToken);
        }

        var tex = Path.Combine(template.Directory, TemplatePackage.TexFolder);
        if (includeTex && Directory.Exists(tex))
        {
            foreach (var file in Directory.EnumerateFiles(tex, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(job, Path.GetRelativePath(tex, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        return template.Filters.Select(f => Path.Combine(TemplateFolder, f)).ToList();
    }

    private static string ConversionError(ProcessOutcome outcome) => outcome switch
    {
        { TimedOut: true } => "Converting the manuscript took too long and was stopped.",
        { ExitCode: 125 } => "The print sandbox could not start: " + outcome.Stderr.Trim(),
        _ => "Converting the manuscript failed; see the log.",
    };

    private static bool Fits(TemplateOption option, JsonElement value) => option.Type switch
    {
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "choice" => value.ValueKind == JsonValueKind.String && (option.Choices ?? []).Contains(value.GetString(), StringComparer.Ordinal),
        _ => false,
    };
}
