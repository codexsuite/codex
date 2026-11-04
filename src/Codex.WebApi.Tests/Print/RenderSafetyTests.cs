using System.Text.Json;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// Author text never becomes markup: hostile knowledge-base fields in the HTML preview, and text the Lua filters
// interpolate into LaTeX commands (drop caps, spell headers), plus a fenced code block in every built-in template.
// Hand-built snapshots go through the real assembler, pandoc and Tectonic (sandboxed, warm cache).
[TestFixture]
public class RenderSafetyTests : ApiFixture
{
    private static readonly Guid Book = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    private static readonly Guid Act = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    private static readonly Guid Chapter = Guid.Parse("00000000-0000-7000-8000-0000000000b3");
    private static readonly Guid Scene = Guid.Parse("00000000-0000-7000-8000-0000000000b4");
    private static readonly Guid Entry = Guid.Parse("00000000-0000-7000-8000-0000000000b5");

    private readonly List<string> _folders = [];

    private IRenderService Renderer => Host.Services.GetRequiredService<IRenderService>();

    [OneTimeTearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static ManuscriptSnapshot Snapshot(string body, string fields = "{}") => new(
        new RenderBook(Book, "Safety", null, "Tester"),
        RenderScope.Chapter,
        [new RenderAct(Act, "Part", null, null)],
        [new RenderChapter(Chapter, Act, "Opening", true, 1, null, null, true)],
        [new RenderScene(Scene, Chapter, body)],
        new Dictionary<Guid, RenderFigure>(),
        new Dictionary<Guid, RenderImage>(),
        new Dictionary<Guid, RenderEntry> { [Entry] = new(Entry, "Ember \"Ward\" <i>", fields, "A ring of embers.") },
        new Dictionary<Guid, RenderCitation>());

    private AssembledDocument Assemble(string slug, string body, string fields = "{}", RenderTarget target = RenderTarget.Pdf)
    {
        var template = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn(slug);
        var options = template.Options.ToDictionary(o => o.Key, o => o.Value.Default);
        if (options.ContainsKey("drop-caps"))
        {
            options["drop-caps"] = JsonSerializer.SerializeToElement(true);
        }
        return ManuscriptAssembler.Assemble(Snapshot(body, fields), template, template.Trims[0], options, target);
    }

    private async Task<(RenderedPdf Pdf, string Tex)> CompileAsync(string slug, string body, string fields = "{}")
    {
        var pdf = await Renderer.CompileAsync(Assemble(slug, body, fields), null, CancellationToken.None);
        _folders.Add(pdf.JobDirectory);
        var tex = await File.ReadAllTextAsync(Path.Combine(pdf.JobDirectory, "main.tex"));
        return (pdf, tex);
    }

    [Test]
    public async Task Html_WhenAnEntryHasHostileFieldNamesAndValues_ShouldWriteNoLiveAttributeOrTag()
    {
        var fields = "{\"onclick\": \"alert(1)\", \"x\\\"><script>alert(2)</script>\": \"y\", \"range\": \"\\\"><script>alert(3)</script>\"}";
        var document = Assemble("fantasy-rpg", $"::spell{{kb=\"{Entry}\"}}\n", fields, RenderTarget.Html);
        var job = Path.Combine(Path.GetTempPath(), "codex-render-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        _folders.Add(job);
        await File.WriteAllTextAsync(Path.Combine(job, "document.json"), document.Ast.ToJsonString());

        var outcome = await Host.Services.GetRequiredService<IPandocRunner>()
            .ConvertAsync(job, "document.json", "html5", null, [], "out.html", CancellationToken.None);
        var html = await File.ReadAllTextAsync(Path.Combine(job, "out.html"));

        outcome.ExitCode.ShouldBe(0, outcome.Stderr);
        html.ShouldNotContain("<script");
        html.ShouldNotContain(" onclick=");
        html.ShouldContain("data-field-onclick=\"alert(1)\"");
        html.ShouldNotContain("<i>");
    }

    [TestCase("fantasy-rpg", "\\DndDropCapLine{1}{00\\%}")]
    [TestCase("novel-6x9", "\\lettrine{1}{00\\%}")]
    public async Task Pdf_WhenAChapterOpensWithAPercentSign_ShouldEscapeTheDropCapAndCompile(string slug, string expected)
    {
        var (pdf, tex) = await CompileAsync(slug, "100% of the gods fell.\n");

        pdf.Error.ShouldBeNull(pdf.Log);
        tex.ShouldContain(expected);
    }

    [TestCase("fantasy-rpg")]
    [TestCase("novel-6x9")]
    public async Task Pdf_WhenAChapterOpensWithABackslashWord_ShouldPrintItAsText(string slug)
    {
        var (pdf, tex) = await CompileAsync(slug, "\\immediate\\openout opened nothing.\n");

        pdf.Error.ShouldBeNull(pdf.Log);
        tex.ShouldNotContain("\\immediate\\openout");
        tex.ShouldContain("\\textbackslash");
    }

    [Test]
    public async Task Pdf_WhenASpellFieldHoldsTexSpecials_ShouldEscapeItInTheSpellHeader()
    {
        var (pdf, tex) = await CompileAsync("fantasy-rpg", $"::spell{{kb=\"{Entry}\"}}\n", "{\"range\": \"30 ft & 10%\", \"duration\": \"\\\\input{/etc/hostname}\"}");

        pdf.Error.ShouldBeNull(pdf.Log);
        tex.ShouldContain("30 ft \\& 10\\%");
        tex.ShouldNotContain("\\input{/etc/hostname}");
    }

    [TestCase("fantasy-rpg")]
    [TestCase("novel-6x9")]
    [TestCase("manuscript")]
    public async Task Pdf_WhenTheSceneHasAFencedCodeBlock_ShouldCompileInEveryTemplate(string slug)
    {
        var (pdf, _) = await CompileAsync(slug, "Before.\n\n```csharp\nvar x = 1;\n```\n\nAfter.\n");

        pdf.Error.ShouldBeNull(pdf.Log);
    }

    [TestCase("fantasy-rpg")]
    [TestCase("novel-6x9")]
    [TestCase("manuscript")]
    public async Task Pdf_WhenTheSceneHasATableRuleStrikeLinkAndInlineLatex_ShouldCompileInEveryTemplate(string slug)
    {
        var body = "Before ~~old~~ [site](https://example.com) and `\\textsc{word}`{=latex}.\n\n"
            + "| Road | Distance | Danger |\n|:---|---:|:---:|\n| **North** | 40 miles | *High* |\n| Coast | 65 miles | Low |\n\n---\n\nAfter.\n";

        var (pdf, _) = await CompileAsync(slug, body);

        pdf.Error.ShouldBeNull(pdf.Log);
    }
}
