using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// The acceptance criterion of issue #13 phase 6 through the real toolchain (needs pandoc, Tectonic and codex-sandbox, as
// CI installs them): fork the novel template, change its CSS and its wrapper, pick it for a book, preview the upgrade
// before applying it, compile a PDF with the fork, and draw the picker's sample.
[TestFixture]
public class UserTemplateRenderTests : ApiFixture
{
    private const string Marker = "% codex-user-template-marker";
    private static readonly Guid Book = SeedIds.Books.PlayersGuide;

    private static Guid _fork100;
    private static Guid _fork110;

    private readonly List<string> _folders = [];

    [OneTimeTearDown]
    public void DeleteFolders() => _folders.ForEach(TemplateZips.Delete);

    private static LayoutPreviewRequest Chapter(Guid? version = null) => new("chapter", SeedIds.Chapters.PlayersGuideCh1, TemplateVersionId: version);

    [Test, Order(1)]
    public async Task Setup_WhenTheNovelIsForkedEditedAndPicked_ShouldPinTheFirstVersion()
    {
        var novel = (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Templates.Single(t => t.Slug == "novel-6x9");
        using var forked = await Client.PostAsJsonAsync($"/api/layout-template-versions/{novel.VersionId}/fork", new ForkTemplateRequest());
        var fork = (await forked.Content.ReadFromJsonAsync<LayoutTemplateVersionDto>())!;
        using var put = await Client.PutAsJsonAsync($"/api/books/{Book}/layout", new LayoutSelectionRequest(fork.Slug));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var zip = await Client.GetByteArrayAsync($"/api/layout-template-versions/{fork.VersionId}/package");
        var folder = TemplateZips.Unpack(zip);
        _folders.Add(folder);
        await File.AppendAllTextAsync(Path.Combine(folder, "preview.css"), "\n.codex-user-fork { color: #123456; }\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "wrapper.tex"), Marker + "\n" + await File.ReadAllTextAsync(Path.Combine(folder, "wrapper.tex")));
        TemplateZips.EditManifest(folder, m =>
        {
            m["version"] = "1.1.0";
            m["changelog"] = new JsonArray("Marked wrapper and CSS");
        });
        using var uploaded = await Client.PostAsync($"/api/layout-templates/{fork.TemplateId}/versions", TemplateZips.Form(TemplateZips.Zip(folder)));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());

        _fork100 = fork.VersionId;
        _fork110 = (await uploaded.Content.ReadFromJsonAsync<LayoutTemplateVersionDto>())!.VersionId;
        (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Template.ShouldBe(fork.Slug);
    }

    [Test, Order(2)]
    public async Task Preview_WhenTheNewerVersionIsNamed_ShouldRenderWithItsCssWithoutRepinning()
    {
        var pinned = (await (await Client.PostAsJsonAsync($"/api/books/{Book}/layout/preview", Chapter())).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;
        var newer = (await (await Client.PostAsJsonAsync($"/api/books/{Book}/layout/preview", Chapter(_fork110))).Content.ReadFromJsonAsync<LayoutPreviewDto>())!;

        (pinned.TemplateVersion, pinned.TemplateVersionId).ShouldBe(("1.0.0", _fork100));
        pinned.Css.ShouldNotBeNull().ShouldNotContain("codex-user-fork");
        (newer.TemplateVersion, newer.TemplateVersionId).ShouldBe(("1.1.0", _fork110));
        newer.Css.ShouldNotBeNull().ShouldContain(".codex-user-fork { color: #123456; }");
        newer.ContentHash.ShouldNotBe(pinned.ContentHash);
        (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Templates.Single(t => t.VersionId == _fork100).LatestVersionId.ShouldBe(_fork110);
    }

    [Test, Order(3)]
    public async Task Pdf_WhenTheBookIsUpgradedToTheEditedVersion_ShouldCompileWithTheForksWrapper()
    {
        using var upgrade = await Client.PostAsJsonAsync($"/api/books/{Book}/layout/upgrade", new LayoutUpgradeRequest());
        upgrade.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var pdf = await Host.Services.GetRequiredService<IRenderService>().RenderPdfAsync(
            new RenderRequest(Book, RenderScope.Chapter, SeedIds.Chapters.PlayersGuideCh1, ImagePurpose: "proxy"), null, CancellationToken.None);

        pdf.Error.ShouldBeNull(pdf.Log);
        pdf.Document.TemplateVersionId.ShouldBe(_fork110);
        (await File.ReadAllTextAsync(Path.Combine(pdf.JobDirectory, "main.tex"))).ShouldStartWith(Marker);
    }

    [Test, Order(4)]
    public async Task Sample_WhenTheFantasyTemplatesIsAsked_ShouldGiveItsSampleAsHtmlWithItsCssAndPage()
    {
        var fantasy = (await Client.GetFromJsonAsync<LayoutStateDto>($"/api/books/{Book}/layout"))!.Templates.Single(t => t.Slug == "fantasy-rpg");

        var sample = (await Client.GetFromJsonAsync<LayoutTemplateSampleDto>($"/api/layout-template-versions/{fantasy.VersionId}/sample"))!;

        sample.Html.ShouldContain("The Gods Have Fallen");
        sample.Html.ShouldContain("readaloud");
        sample.Css.ShouldNotBeNull().ShouldContain(".readaloud");
        (sample.Trim, sample.WidthIn, sample.HeightIn, sample.Columns).ShouldBe(("us-letter", 8.5, 11, 2));
    }
}
