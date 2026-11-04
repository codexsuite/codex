using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Print.Templates;

// Reads layout template packages (issue #13 part 5) from disk. A package is a folder:
//   template.json      the manifest (TemplateManifest)
//   wrapper.tex        a pandoc template ($body$, $title$ and the metadata the assembler sets)
//   filters/*.lua      pandoc Lua filters named by the manifest
//   preview.css        optional: Paged.js styles for Quick preview
//   tex/               classes, styles, fonts and images, copied to the job folder's root
//   sample.md          the picker thumbnail's source
// The built-in packages are the folders under CODEX_TEMPLATES_ROOT (the repo's templates/), one per slug.
public interface ITemplateLoader
{
    // Every built-in package, by slug. A folder without template.json is not a package and is left out.
    IReadOnlyList<TemplatePackage> ListBuiltIn();

    // The built-in package with this slug. 404 when there is none.
    TemplatePackage GetBuiltIn(string slug);

    // Reads one package folder and reports what is wrong with it (TemplatePackage.Problems, .Notices); never
    // throws for a bad package, only for a folder that does not exist (404) or a manifest that is not JSON (400).
    TemplatePackage Load(string directory);
}

[RegisterSingleton]
public sealed partial class TemplateLoader(RenderSettings settings) : ITemplateLoader
{
    private static readonly string[] Engines = ["tectonic"];

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
    private static partial Regex SemVer();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex SlugPattern();

    // Lower-case letters, digits and hyphens, starting with a letter or digit: a package folder's and a template's slug.
    public static bool IsSlug(string slug) => SlugPattern().IsMatch(slug);

    // major.minor.patch with no leading zeros.
    public static bool IsVersion(string version) => SemVer().IsMatch(version);

    public IReadOnlyList<TemplatePackage> ListBuiltIn()
    {
        if (!Directory.Exists(settings.TemplatesRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(settings.TemplatesRoot)
            .Where(d => File.Exists(Path.Combine(d, TemplatePackage.ManifestFile)))
            .Order(StringComparer.Ordinal)
            .Select(Load)
            .ToList();
    }

    public TemplatePackage GetBuiltIn(string slug)
    {
        var directory = SlugPattern().IsMatch(slug) ? Path.Combine(settings.TemplatesRoot, slug) : null;
        if (directory is null || !File.Exists(Path.Combine(directory, TemplatePackage.ManifestFile)))
        {
            throw ApiException.NotFound($"There is no built-in layout template '{slug}'.");
        }

        return Load(directory);
    }

    public TemplatePackage Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw ApiException.NotFound($"The template folder {Path.GetFileName(directory)} does not exist.");
        }

        var problems = new List<string>();
        var notices = new List<string>();
        var manifest = ReadManifest(directory, problems);
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        var slug = manifest.Slug ?? folder;
        if (!SlugPattern().IsMatch(slug))
        {
            problems.Add($"The slug '{slug}' must be lower-case letters, digits and hyphens.");
        }
        else if (slug != folder)
        {
            problems.Add($"The slug '{slug}' must match the package folder '{folder}'.");
        }

        var version = manifest.Version ?? string.Empty;
        if (!SemVer().IsMatch(version))
        {
            problems.Add($"The version '{version}' is not major.minor.patch.");
        }

        var engine = manifest.Engine ?? string.Empty;
        if (!Engines.Contains(engine, StringComparer.Ordinal))
        {
            problems.Add($"The engine '{engine}' is not supported; Codex compiles templates with Tectonic (engine \"tectonic\").");
        }

        var trims = manifest.Trims ?? [];
        if (trims.Count == 0)
        {
            problems.Add("The manifest lists no trims.");
        }
        problems.AddRange(trims.Where(t => !TrimSizes.IsKnown(t)).Select(t =>
            $"The trim '{t}' is not one Codex knows ({string.Join(", ", TrimSizes.Known)})."));

        var page = ReadPage(manifest.Page, problems);
        var citations = manifest.Citations switch
        {
            null or "footnote" => CitationForm.Footnote,
            "author-date" => CitationForm.AuthorDate,
            var other => Problem(problems, $"The citation form '{other}' is not footnote or author-date.", CitationForm.Footnote),
        };

        var options = manifest.Options ?? new Dictionary<string, TemplateOption>();
        foreach (var (name, option) in options)
        {
            CheckOption(name, option, problems);
        }
        problems.AddRange(options.Keys.Where(ManuscriptAssembler.ReservedMeta.Contains).Select(name =>
            $"The option '{name}' clashes with metadata Codex sets itself; rename it."));

        var filters = manifest.Filters ?? [];
        var files = (manifest.Files ?? new Dictionary<string, ManifestFile>()).Keys.ToList();
        foreach (var required in filters.Prepend(TemplatePackage.WrapperFile).Distinct(StringComparer.Ordinal))
        {
            if (!InPackage(directory, required))
            {
                problems.Add($"The file {required} is outside the package.");
            }
            else if (!File.Exists(Path.Combine(directory, required)))
            {
                problems.Add($"The required file {required} is missing.");
            }
        }
        problems.AddRange(files
            .Where(f => f != TemplatePackage.PreviewCssFile && (!InPackage(directory, f) || !File.Exists(Path.Combine(directory, f))))
            .Select(f => $"The manifest lists {f}, which is not in the package."));

        var hasPreviewCss = File.Exists(Path.Combine(directory, TemplatePackage.PreviewCssFile));
        if (!hasPreviewCss)
        {
            notices.Add(TemplatePackage.NoPreviewCssNotice);
        }

        return new TemplatePackage(
            slug,
            manifest.Name ?? slug,
            version,
            engine,
            Path.GetFullPath(directory),
            trims,
            page,
            citations,
            manifest.RawLatex,
            filters,
            options,
            manifest.Directives ?? new Dictionary<string, string>(),
            files,
            hasPreviewCss,
            problems,
            notices)
        {
            ContentHash = HashFiles(directory),
            Changelog = manifest.Changelog ?? [],
        };
    }

    private static string HashFiles(string directory)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/'))
                     .Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file + "\0"));
            hash.AppendData(File.ReadAllBytes(Path.Combine(directory, file)));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static TemplateManifest ReadManifest(string directory, List<string> problems)
    {
        var path = Path.Combine(directory, TemplatePackage.ManifestFile);
        if (!File.Exists(path))
        {
            problems.Add($"The package has no {TemplatePackage.ManifestFile}.");
            return new TemplateManifest();
        }

        try
        {
            return JsonSerializer.Deserialize<TemplateManifest>(File.ReadAllText(path), TemplateManifest.Json) ?? new TemplateManifest();
        }
        catch (JsonException e)
        {
            throw ApiException.Validation($"{TemplatePackage.ManifestFile} is not valid JSON: {e.Message}");
        }
    }

    private static PageLayout ReadPage(ManifestPage? page, List<string> problems)
    {
        if (page?.Margins is not { } margins)
        {
            problems.Add("The manifest has no page margins (page.margins top, bottom, inner, outer).");
            return PageLayout.Default;
        }

        if (page.Columns is < 1 or > TrimSizes.MaxColumns)
        {
            problems.Add($"page.columns must be between 1 and {TrimSizes.MaxColumns}.");
        }
        if (new[] { margins.Top, margins.Bottom, margins.Inner, margins.Outer, page.ColumnGap }.Any(v => v < 0 || v > 3))
        {
            problems.Add("Page margins and the column gap must be between 0 and 3 inches.");
        }

        return new PageLayout(margins.Top, margins.Bottom, margins.Inner, margins.Outer, page.ColumnGap, Math.Clamp(page.Columns, 1, TrimSizes.MaxColumns));
    }

    private static void CheckOption(string name, TemplateOption option, List<string> problems)
    {
        switch (option.Type)
        {
            case "boolean" when option.Default.ValueKind is JsonValueKind.True or JsonValueKind.False:
                break;
            case "choice" when option.Choices is { Count: > 0 } choices
                               && option.Default.ValueKind == JsonValueKind.String
                               && choices.Contains(option.Default.GetString(), StringComparer.Ordinal):
                break;
            default:
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"The option '{name}' needs type boolean with a true/false default, or type choice with choices and a default among them."));
                break;
        }
    }

    private static bool InPackage(string directory, string relative)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        return !Path.IsPathRooted(relative) && Path.GetFullPath(Path.Combine(directory, relative)).StartsWith(root, StringComparison.Ordinal);
    }

    private static T Problem<T>(List<string> problems, string problem, T fallback)
    {
        problems.Add(problem);
        return fallback;
    }
}
