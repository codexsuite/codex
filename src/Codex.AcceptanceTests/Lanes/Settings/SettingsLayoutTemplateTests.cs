using System.IO.Compression;
using System.Text.Json.Nodes;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Lanes.Settings;

// Template management in Settings → Layout & print (#13 phase 6) on the Players Guide: fork Fantasy RPG, use the fork,
// upload an edited copy as 1.1.0 and apply the upgrade. Thumbnails need pandoc, which this lane does not assume; a
// card without one shows a blank page.
[TestFixture]
public class SettingsLayoutTemplateTests : SeededAcceptanceTest
{
    private static readonly string Book = SeedIds.Books.PlayersGuide.ToString();

    private readonly List<string> _folders = [];

    private SettingsScreen Settings => new(Page);

    [OneTimeTearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The repo's fantasy-rpg package as the fork's 1.1.0, with a changed preview.css.
    private string ForkVersionZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-acceptance-templates", Guid.NewGuid().ToString("N"));
        _folders.Add(root);
        var package = Path.Combine(root, "fantasy-rpg-fork");
        var source = Path.Combine(RepoPaths.Root, "templates", "fantasy-rpg");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(package, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        File.AppendAllText(Path.Combine(package, "preview.css"), "\nbody { color: #2b1d0e; }\n");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "template.json")))!.AsObject();
        manifest["slug"] = "fantasy-rpg-fork";
        manifest["version"] = "1.1.0";
        manifest["changelog"] = new JsonArray("Darker body text");
        File.WriteAllText(Path.Combine(package, "template.json"), manifest.ToJsonString());
        var zip = Path.Combine(root, "fantasy-rpg-fork-1.1.0.zip");
        ZipFile.CreateFromDirectory(package, zip);
        return zip;
    }

    [Test, Order(1)]
    public async Task Fork_WhenConfirmedAndUsed_ShouldPinTheBookToTheFork()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("layout", null))));
        await Settings.LayoutTemplatesToggle.ClickAsync();
        await Settings.LayoutTemplateFork("fantasy-rpg").ClickAsync();
        await Expect(Settings.LayoutForkName).ToHaveValueAsync("Fantasy RPG (5e style) (fork)");

        var forked = await Page.RunAndWaitForResponseAsync(
            () => Settings.LayoutForkConfirm.ClickAsync(),
            r => r.Request.Method == "POST" && r.Url.EndsWith("/fork", StringComparison.Ordinal));
        forked.Status.ShouldBe(201);
        await Expect(Settings.LayoutTemplateCard("fantasy-rpg-fork")).ToContainTextAsync("Forked from Fantasy RPG (5e style) v1.0.0");

        var used = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/layout", "PUT", () => Settings.LayoutTemplateUse("fantasy-rpg-fork").ClickAsync());

        used.Status.ShouldBe(200);
        await Expect(Settings.LayoutTemplateCard("fantasy-rpg-fork")).ToHaveAttributeAsync("aria-current", "true");
        await Expect(Settings.LayoutTemplatePick).ToHaveValueAsync("fantasy-rpg-fork");
    }

    [Test, Order(2)]
    public async Task UploadVersion_WhenAnEditedPackageIsUploaded_ShouldOfferTheUpgradeAndApplyIt()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("layout", null))));
        await Settings.LayoutTemplatesToggle.ClickAsync();
        var chooser = await Page.RunAndWaitForFileChooserAsync(() => Settings.LayoutTemplateUploadVersion("fantasy-rpg-fork").ClickAsync());

        var uploaded = await Page.RunAndWaitForResponseAsync(
            () => chooser.SetFilesAsync(ForkVersionZip()),
            r => r.Request.Method == "POST" && r.Url.EndsWith("/versions", StringComparison.Ordinal));

        uploaded.Status.ShouldBe(201);
        await Expect(Settings.LayoutChangelog).ToHaveTextAsync("Darker body text");
        var applied = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/layout/upgrade", "POST", () => Settings.LayoutUpgradeApply.ClickAsync());
        applied.Status.ShouldBe(200);
        await Expect(Settings.LayoutTemplateCard("fantasy-rpg-fork")).ToContainTextAsync("v1.1.0");
        await Expect(Settings.LayoutChangelog).ToHaveCountAsync(0);
    }
}
