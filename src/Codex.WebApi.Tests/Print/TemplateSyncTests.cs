using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Codex.WebApi.Tests.Print;

// The built-in sync when the app ships a newer built-in: a copy of the templates folder where novel-6x9 is 1.1.0 plays
// the new release. Its sync adds the version, which a book pinned to 1.0.0 is offered and not moved to; and the new
// release (whose disk has only 1.1.0) still opens 1.0.0, from the zip the old release stored.
[TestFixture]
public class TemplateSyncTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.EmberOath;

    private string _root = string.Empty;

    private RenderSettings NextRelease => Host.Services.GetRequiredService<RenderSettings>() with { TemplatesRoot = _root };

    private BuiltInTemplateSync SyncOf(RenderSettings settings) => new(
        Host.Services.GetRequiredService<NpgsqlDataSource>(), new TemplateLoader(settings), NullLogger<BuiltInTemplateSync>.Instance);

    [OneTimeSetUp]
    public void CopyTheTemplatesWithANewerNovel()
    {
        var source = Host.Services.GetRequiredService<RenderSettings>().TemplatesRoot;
        _root = Path.Combine(Path.GetTempPath(), "codex-template-sync-tests", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        TemplateZips.EditManifest(Path.Combine(_root, "novel-6x9"), m =>
        {
            m["version"] = "1.1.0";
            m["changelog"] = new JsonArray("Wider inner margin");
        });
    }

    [OneTimeTearDown]
    public void DeleteTheCopy()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test, Order(1)]
    public async Task Sync_WhenRunAgainOnTheSameRelease_ShouldChangeNothing()
    {
        (await SyncOf(Host.Services.GetRequiredService<RenderSettings>()).SyncAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Sync_WhenANewerBuiltInShips_ShouldAddItAsTheUpgradeAndLeaveThePinnedBookOnItsVersion()
    {
        using var put = await Client.PutAsJsonAsync($"/api/books/{Book}/layout", new LayoutSelectionRequest("novel-6x9"));
        put.EnsureSuccessStatusCode();

        var added = await SyncOf(NextRelease).SyncAsync(CancellationToken.None);
        var again = await SyncOf(NextRelease).SyncAsync(CancellationToken.None);
        var novel = (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Templates.Single(t => t.Slug == "novel-6x9");

        (added, again).ShouldBe((1, 0));
        (novel.Version, novel.LatestVersion).ShouldBe(("1.0.0", "1.1.0"));
        novel.LatestChangelog.ShouldBe(["Wider inner margin"]);
        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string Pinned, bool Upgrade)>(
            "SELECT pinned_version, upgrade_available FROM book_layout_current WHERE book_id = @Book", new { Book })).ShouldBe(("1.0.0", true));
    }

    [Test, Order(3)]
    public async Task Open_WhenTheDiskHasOnlyTheNewerVersion_ShouldUnpackThePinnedOneFromItsStoredZip()
    {
        var settings = NextRelease with { TemplateCache = Path.Combine(_root, ".cache") };
        var store = new TemplateStore(Host.Services.GetRequiredService<NpgsqlDataSource>(), new TemplateLoader(settings), settings);
        var shipped = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn("novel-6x9");
        await using var db = await OpenDatabaseAsync();
        var versions = (await db.QueryAsync<(Guid Id, string Version)>(
            """
            SELECT v.id, v.version FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id
            WHERE t.owner_id IS NULL AND t.slug = 'novel-6x9' ORDER BY v.version
            """)).ToDictionary(v => v.Version, v => v.Id);

        var old = await store.OpenAsync(versions["1.0.0"], CancellationToken.None);
        var current = await store.OpenAsync(versions["1.1.0"], CancellationToken.None);

        (old.Version, old.ContentHash, old.CanCompile).ShouldBe(("1.0.0", shipped.ContentHash, true));
        old.Directory.ShouldStartWith(settings.TemplateCache);
        current.Directory.ShouldBe(Path.Combine(_root, "novel-6x9"));
    }
}
