using Codex.Application.Features.Print;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// The template package loader over the repo's templates/ (the host's CODEX_TEMPLATES_ROOT) and over broken copies.
[TestFixture]
public class TemplatePackageTests : ApiFixture
{
    private readonly List<string> _folders = [];

    private ITemplateLoader Loader => Host.Services.GetRequiredService<ITemplateLoader>();

    [OneTimeTearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A copy of a built-in package that a test may break.
    private string CopyOf(string slug)
    {
        var source = Loader.GetBuiltIn(slug).Directory;
        var target = Path.Combine(Path.GetTempPath(), "codex-template-tests", Guid.NewGuid().ToString("N"), slug);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
        _folders.Add(Path.GetDirectoryName(target)!);
        return target;
    }

    [Test]
    public async Task ListBuiltIn_WhenTheRepoTemplatesAreRead_ShouldMatchTheSeedSlugsAndHaveNoProblems()
    {
        await using var connection = await OpenDatabaseAsync();
        var seeded = (await connection.QueryAsync<string>("SELECT slug FROM layout_template WHERE owner_id IS NULL ORDER BY slug")).ToList();

        var packages = Loader.ListBuiltIn();

        packages.Select(p => p.Slug).ShouldBe(seeded);
        foreach (var package in packages)
        {
            package.Problems.ShouldBeEmpty(package.Slug);
            package.HasPreviewCss.ShouldBeTrue(package.Slug);
            package.Engine.ShouldBe("tectonic");
        }
    }

    [Test]
    public void Load_WhenTheFantasyTemplateIsRead_ShouldGiveItsOwnPageLayout()
    {
        var package = Loader.GetBuiltIn("fantasy-rpg");

        package.Page.ShouldBe(new PageLayout(0.46, 0.8, 0.75, 0.75, 0.33, 2));
        package.AllowsRawLatex.ShouldBeTrue();
        package.Citations.ShouldBe(CitationForm.Footnote);
        package.Trims.ShouldBe(["us-letter", "a4"]);
    }

    [Test]
    public void Load_WhenThePackageHasNoPreviewCss_ShouldReportItWithoutBlockingTheCompile()
    {
        var folder = CopyOf("novel-6x9");
        File.Delete(Path.Combine(folder, TemplatePackage.PreviewCssFile));

        var package = Loader.Load(folder);

        package.HasPreviewCss.ShouldBeFalse();
        package.Notices.ShouldBe([TemplatePackage.NoPreviewCssNotice]);
        package.CanCompile.ShouldBeTrue();
    }

    [Test]
    public void Load_WhenTheWrapperIsMissingAndATrimIsUnknown_ShouldListBothProblems()
    {
        var folder = CopyOf("manuscript");
        File.Delete(Path.Combine(folder, TemplatePackage.WrapperFile));
        var manifest = Path.Combine(folder, TemplatePackage.ManifestFile);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"a4\"", "\"b5\""));

        var package = Loader.Load(folder);

        package.CanCompile.ShouldBeFalse();
        package.Problems.ShouldContain("The required file wrapper.tex is missing.");
        package.Problems.ShouldContain(p => p.StartsWith("The trim 'b5'", StringComparison.Ordinal));
    }

    [Test]
    public void GetBuiltIn_WhenTheSlugTriesToLeaveTheTemplatesFolder_ShouldBeNotFound()
    {
        Should.Throw<ApiException>(() => Loader.GetBuiltIn("../templates")).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public void Load_WhenTheWrapperIsEdited_ShouldChangeThePackageHash()
    {
        var folder = CopyOf("novel-6x9");
        var before = Loader.Load(folder).ContentHash;
        File.AppendAllText(Path.Combine(folder, TemplatePackage.WrapperFile), "% edited\n");

        var after = Loader.Load(folder).ContentHash;

        before.ShouldBe(Loader.GetBuiltIn("novel-6x9").ContentHash);
        after.ShouldNotBe(before);
    }

    [Test]
    public void Load_WhenTheSlugDiffersFromTheFolderOrAnOptionUsesAReservedName_ShouldListBothProblems()
    {
        var folder = CopyOf("manuscript");
        var manifest = Path.Combine(folder, TemplatePackage.ManifestFile);
        File.WriteAllText(manifest, File.ReadAllText(manifest)
            .Replace("\"slug\": \"manuscript\"", "\"slug\": \"other\"")
            .Replace("\"font\": {", "\"papersize\": { \"type\": \"boolean\", \"default\": true },\n    \"font\": {"));

        var package = Loader.Load(folder);

        package.Problems.ShouldContain("The slug 'other' must match the package folder 'manuscript'.");
        package.Problems.ShouldContain("The option 'papersize' clashes with metadata Codex sets itself; rename it.");
    }
}
