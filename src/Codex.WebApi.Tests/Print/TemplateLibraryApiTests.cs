using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// Template management (issue #13 phase 6) on JD's two RPG books: fork Fantasy RPG, pick the fork for both books,
// download it, change its CSS and upload it as 1.1.0, upgrade one book, and archive the fork once no book uses it.
// Nothing here runs pandoc or Tectonic: resolving, pinning, preflight and queuing a proof only open the packages.
[TestFixture]
public class TemplateLibraryApiTests : ApiFixture
{
    private static readonly Guid BookA = SeedIds.Books.PlayersGuide;
    private static readonly Guid BookB = SeedIds.Books.GamemastersCodex;

    private static Guid _forkId;
    private static Guid _fork100;
    private static Guid _fork110;
    private static byte[] _forkZip = [];

    private readonly List<string> _folders = [];

    [OneTimeTearDown]
    public void DeleteFolders() => _folders.ForEach(TemplateZips.Delete);

    private async Task<LayoutStateDto> StateAsync(Guid book) => (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{book}/layout"))!;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] zip, Guid? templateId = null)
        => await Client.PostAsync(templateId is { } id ? $"/api/layout-templates/{id}/versions" : "/api/layout-templates", TemplateZips.Form(zip));

    // The fork's package, changed by edit, as a zip.
    private byte[] ForkWith(Action<string> edit)
    {
        var folder = TemplateZips.Unpack(_forkZip);
        _folders.Add(folder);
        edit(folder);
        return TemplateZips.Zip(folder);
    }

    [Test, Order(1)]
    public async Task Sync_WhenTheHostStarted_ShouldHaveStoredEveryBuiltInPackageOnItsSeedVersionRow()
    {
        var loader = Host.Services.GetRequiredService<ITemplateLoader>();
        await using var db = await OpenDatabaseAsync();

        var rows = (await db.QueryAsync<(string Slug, string Version, string? Sha, bool HasZip, int Versions)>(
            """
            SELECT t.slug, v.version, v.package_sha256, v.package_zip IS NOT NULL, count(*) OVER (PARTITION BY t.id)::int
            FROM layout_template t JOIN layout_template_version v ON v.template_id = t.id
            WHERE t.owner_id IS NULL ORDER BY t.slug
            """)).ToList();

        rows.Select(r => r.Slug).ShouldBe(["fantasy-rpg", "manuscript", "novel-6x9"]);
        foreach (var row in rows)
        {
            var disk = loader.GetBuiltIn(row.Slug);
            (row.Version, row.Sha, row.HasZip, row.Versions).ShouldBe((disk.Version, disk.ContentHash, true, 1), row.Slug);
        }
    }

    [Test, Order(2)]
    public async Task Fork_WhenABuiltInIsForkedTwice_ShouldMakeTwoTemplatesOfTheUsersAt100()
    {
        var fantasy = (await StateAsync(BookA)).Templates.Single(t => t.Slug == "fantasy-rpg");

        var fork = await ReadAsync<LayoutTemplateVersionDto>(
            await Client.PostAsJsonAsync($"/api/layout-template-versions/{fantasy.VersionId}/fork", new ForkTemplateRequest()), HttpStatusCode.Created);
        var second = await ReadAsync<LayoutTemplateVersionDto>(
            await Client.PostAsJsonAsync($"/api/layout-template-versions/{fantasy.VersionId}/fork", new ForkTemplateRequest("Forsaken RPG")), HttpStatusCode.Created);

        (fork.Slug, fork.Name, fork.Version, fork.IsUserTemplate).ShouldBe(("fantasy-rpg-fork", "Fantasy RPG (5e style) (fork)", "1.0.0", true));
        fork.ForkedFrom.ShouldBe("Fantasy RPG (5e style) v1.0.0");
        fork.Changelog.ShouldBe(["Forked from Fantasy RPG (5e style) v1.0.0"]);
        (second.Slug, second.Name).ShouldBe(("fantasy-rpg-fork-2", "Forsaken RPG"));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT owner_id FROM layout_template WHERE id = @TemplateId", fork)).ShouldBe(SeedIds.Users.Jd);
        _forkId = fork.TemplateId;
        _fork100 = fork.VersionId;
    }

    [Test, Order(3)]
    public async Task Download_WhenTheForkIsDownloaded_ShouldGiveItsFilesWithTheForksManifest()
    {
        using var response = await Client.GetAsync($"/api/layout-template-versions/{_fork100}/package");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("fantasy-rpg-fork-1.0.0.zip");
        _forkZip = await response.Content.ReadAsByteArrayAsync();

        var builtIn = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn("fantasy-rpg").Directory;
        var files = Directory.EnumerateFiles(builtIn, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(builtIn, f).Replace(Path.DirectorySeparatorChar, '/'));
        using var zip = new ZipArchive(new MemoryStream(_forkZip));
        zip.Entries.Select(e => e.FullName).ShouldBe(files, ignoreOrder: true);
        using var manifest = new StreamReader(await zip.GetEntry(TemplatePackage.ManifestFile)!.OpenAsync());
        var json = JsonNode.Parse(await manifest.ReadToEndAsync())!;
        ((string?)json["slug"], (string?)json["version"]).ShouldBe(("fantasy-rpg-fork", "1.0.0"));
    }

    [Test, Order(4)]
    public async Task Select_WhenBothBooksPickTheFork_ShouldPinItAndListItWithWhereItCameFrom()
    {
        foreach (var book in new[] { BookA, BookB })
        {
            using var put = await Client.PutAsJsonAsync($"/api/books/{book}/layout", new LayoutSelectionRequest("fantasy-rpg-fork"));
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        }

        var state = await StateAsync(BookA);

        state.Template.ShouldBe("fantasy-rpg-fork");
        state.Trim.ShouldBe("us-letter");
        state.Templates.Select(t => t.Slug).ShouldBe(["fantasy-rpg", "manuscript", "novel-6x9", "fantasy-rpg-fork", "fantasy-rpg-fork-2"]);
        var fork = state.Templates.Single(t => t.Slug == "fantasy-rpg-fork");
        (fork.IsUserTemplate, fork.Version, fork.VersionId, fork.ForkedFrom, fork.LatestVersion).ShouldBe((true, "1.0.0", _fork100, "Fantasy RPG (5e style) v1.0.0", null));
        fork.Options.Select(o => o.Key).ShouldContain("drop-caps");
    }

    [Test, Order(5)]
    public async Task UploadVersion_WhenTheCssChangesAndTheVersionIsRaised_ShouldOfferItAsTheUpgradeWithItsChangelog()
    {
        var zip = ForkWith(folder =>
        {
            File.AppendAllText(Path.Combine(folder, TemplatePackage.PreviewCssFile), "\nbody { color: #123456; }\n");
            TemplateZips.EditManifest(folder, m =>
            {
                m["version"] = "1.1.0";
                m["changelog"] = new JsonArray("Darker body text");
            });
        });

        var created = await ReadAsync<LayoutTemplateVersionDto>(await UploadAsync(zip, _forkId), HttpStatusCode.Created);
        var fork = (await StateAsync(BookA)).Templates.Single(t => t.Slug == "fantasy-rpg-fork");

        (created.Version, created.Changelog.Single()).ShouldBe(("1.1.0", "Darker body text"));
        (fork.Version, fork.LatestVersion, fork.LatestVersionId).ShouldBe(("1.0.0", "1.1.0", created.VersionId));
        fork.LatestChangelog.ShouldBe(["Darker body text"]);
        _fork110 = created.VersionId;
    }

    [Test, Order(6)]
    public async Task Proof_WhenTheBookIsPinnedToTheForksFirstVersion_ShouldQueueItWithThatVersion()
    {
        var proof = await ReadAsync<ProofDto>(
            await Client.PostAsJsonAsync($"/api/books/{BookA}/proofs", new ProofRequest(SeedIds.Chapters.PlayersGuideCh1)), HttpStatusCode.Accepted);

        (proof.TemplateSlug, proof.TemplateVersion).ShouldBe(("fantasy-rpg-fork", "1.0.0"));
    }

    [Test, Order(7)]
    public async Task Upgrade_WhenAppliedToOneBook_ShouldRepinItAndLeaveTheOtherBookOnItsVersion()
    {
        var state = await ReadAsync<LayoutStateDto>(
            await Client.PostAsJsonAsync($"/api/books/{BookA}/layout/upgrade", new LayoutUpgradeRequest()), HttpStatusCode.OK);
        var preflight = await ReadAsync<PreflightDto>(
            await Client.PostAsJsonAsync($"/api/books/{BookA}/export/preflight", new ExportRequest()), HttpStatusCode.OK);

        var fork = state.Templates.Single(t => t.Slug == "fantasy-rpg-fork");
        (fork.Version, fork.VersionId, fork.LatestVersion).ShouldBe(("1.1.0", _fork110, null));
        state.Trim.ShouldBe("us-letter");
        (preflight.Template, preflight.TemplateVersion).ShouldBe(("fantasy-rpg-fork", "1.1.0"));
        var other = (await StateAsync(BookB)).Templates.Single(t => t.Slug == "fantasy-rpg-fork");
        (other.Version, other.LatestVersion).ShouldBe(("1.0.0", "1.1.0"));
    }

    [Test, Order(8)]
    public async Task Upgrade_WhenTheVersionBelongsToAnotherTemplate_ShouldBeValidation()
    {
        var novel = (await StateAsync(BookA)).Templates.Single(t => t.Slug == "novel-6x9");

        using var response = await Client.PostAsJsonAsync($"/api/books/{BookA}/layout/upgrade", new LayoutUpgradeRequest(novel.VersionId));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(9)]
    public async Task UploadVersion_WhenTheVersionIsNotNewer_ShouldBeValidation()
    {
        using var response = await UploadAsync(ForkWith(_ => { }), _forkId);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("not newer than v1.1.0");
    }

    [Test, Order(10)]
    public async Task UploadVersion_WhenTheSlugIsAnotherTemplates_ShouldBeValidation()
    {
        var zip = ForkWith(folder => TemplateZips.EditManifest(folder, m =>
        {
            m["slug"] = "something-else";
            m["version"] = "2.0.0";
        }));

        using var response = await UploadAsync(zip, _forkId);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("new version of 'fantasy-rpg-fork'");
    }

    [Test, Order(11)]
    public async Task Upload_WhenThePackageMissesItsWrapper_ShouldBeValidationWithTheLoadersProblem()
    {
        var zip = ForkWith(folder =>
        {
            File.Delete(Path.Combine(folder, TemplatePackage.WrapperFile));
            TemplateZips.EditManifest(folder, m => m["slug"] = "broken-one");
        });

        using var response = await UploadAsync(zip);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("The required file wrapper.tex is missing.");
    }

    [Test, Order(12)]
    public async Task Upload_WhenTheSlugIsABuiltIns_ShouldBeConflict()
    {
        var zip = ForkWith(folder => TemplateZips.EditManifest(folder, m => m["slug"] = "novel-6x9"));

        using var response = await UploadAsync(zip);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(13)]
    public async Task Upload_WhenAnEntryClimbsOutOfThePackage_ShouldBeValidationAndAddNoTemplate()
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = new StreamWriter(await zip.CreateEntry("../../evil.lua").OpenAsync());
            await entry.WriteAsync("os.execute('id')");
        }

        using var response = await UploadAsync(buffer.ToArray());

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("not a plain relative path");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM layout_template WHERE owner_id IS NOT NULL")).ShouldBe(2);
    }

    [Test, Order(14)]
    public async Task Upload_WhenANewSlugIsUploaded_ShouldMakeANewTemplateNotForkedFromAnything()
    {
        var zip = ForkWith(folder => TemplateZips.EditManifest(folder, m =>
        {
            m["slug"] = "zine";
            m["name"] = "Zine";
            m["version"] = "0.1.0";
        }));

        var created = await ReadAsync<LayoutTemplateVersionDto>(await UploadAsync(zip), HttpStatusCode.Created);

        (created.Slug, created.Name, created.Version, created.ForkedFrom).ShouldBe(("zine", "Zine", "0.1.0", null));
        (await StateAsync(BookA)).Templates.Select(t => t.Slug).ShouldContain("zine");
    }

    [Test, Order(15)]
    public async Task Archive_WhenABookStillUsesTheTemplate_ShouldBeConflict()
    {
        using var response = await Client.PostAsync($"/api/layout-templates/{_forkId}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Error.ShouldContain("Choose another template for those books first.");
    }

    [Test, Order(16)]
    public async Task Archive_WhenNoBookUsesItAnyMore_ShouldHideItFromThePicker()
    {
        foreach (var book in new[] { BookA, BookB })
        {
            using var put = await Client.PutAsJsonAsync($"/api/books/{book}/layout", new LayoutSelectionRequest("novel-6x9"));
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await Client.PostAsync($"/api/layout-templates/{_forkId}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StateAsync(BookA)).Templates.Select(t => t.Slug).ShouldNotContain("fantasy-rpg-fork");
    }

    [Test, Order(17)]
    public async Task Archive_WhenTheTemplateIsBuiltIn_ShouldBeValidation()
    {
        var novel = (await StateAsync(BookA)).Templates.Single(t => t.Slug == "novel-6x9");

        using var response = await Client.PostAsync($"/api/layout-templates/{novel.TemplateId}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("built in");
    }
}
