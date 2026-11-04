using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Application.Prose;
using Codex.TestSupport;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// #64 acceptance: the table-heavy mapped Players Guide chapters (Fixtures/players-guide/mapped) go through the real
// assembler, the Fantasy RPG template's Lua filter and pandoc to LaTeX. Every pipe table is a DndTable, cell
// formatting survives, and nothing unfenced prints as literal LaTeX.
[TestFixture]
public partial class PlayersGuideTablesPrintTests : ApiFixture
{
    private static readonly Guid Book = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid Act = Guid.Parse("00000000-0000-7000-8000-0000000000c2");
    private static readonly Guid Chapter = Guid.Parse("00000000-0000-7000-8000-0000000000c3");
    private static readonly Guid Scene = Guid.Parse("00000000-0000-7000-8000-0000000000c4");

    private const string FormattedTable = "\n| Plain | Formatted |\n|---|---|\n| *slanted* | **heavy** and *light* |\n";

    private readonly List<string> _folders = [];

    [GeneratedRegex(@"\\begin\{DndTable\}")]
    private static partial Regex DndTableStart();

    [OneTimeTearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Mapped(string chapter) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "players-guide", "mapped", chapter + ".md"));

    private async Task<RenderedPdf> RenderAsync(string body)
    {
        var template = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn("fantasy-rpg");
        var options = template.Options.ToDictionary(o => o.Key, o => o.Value.Default);
        var snapshot = new ManuscriptSnapshot(
            new RenderBook(Book, "Players Guide", null, "Tester"),
            RenderScope.Chapter,
            [new RenderAct(Act, "Part", null, null)],
            [new RenderChapter(Chapter, Act, "Chapter", true, 1, null, null, true)],
            [new RenderScene(Scene, Chapter, body)],
            new Dictionary<Guid, RenderFigure>(),
            new Dictionary<Guid, RenderImage>(),
            new Dictionary<Guid, RenderEntry>(),
            new Dictionary<Guid, RenderCitation>());
        var document = ManuscriptAssembler.Assemble(snapshot, template, template.Trims[0], options, RenderTarget.Pdf);
        var pdf = await Host.Services.GetRequiredService<IRenderService>().CompileAsync(document, null, CancellationToken.None);
        _folders.Add(pdf.JobDirectory);
        return pdf;
    }

    private async Task<string> TexAsync(string body) =>
        await File.ReadAllTextAsync(Path.Combine((await RenderAsync(body)).JobDirectory, "main.tex"));

    // The fixture's page-sized figures are raw LaTeX fences that name image files this repository does not hold.
    [GeneratedRegex(@"```\{=latex\}\n(?:(?!```\n).)*?\\includegraphics.*?```\n", RegexOptions.Singleline)]
    private static partial Regex FigureFence();

    [TestCase("chapter-03")]
    [TestCase("chapter-04")]
    public async Task Tex_WhenAMappedChapterHasPipeTables_ShouldDrawEachAsADndTable(string chapter)
    {
        var body = Mapped(chapter) + FormattedTable;
        var tables = ProseDocument.Parse(body).Blocks.Count(b => b.Kind == ProseBlockKind.Table);

        var tex = await TexAsync(body);

        tables.ShouldBeGreaterThan(1);
        DndTableStart().Matches(tex).Count.ShouldBe(tables);
        Regex.Matches(tex, @"\\end\{DndTable\}").Count.ShouldBe(tables);
    }

    [Test]
    public async Task Tex_WhenCellsHoldBoldAndItalic_ShouldKeepThemInsideTheDndTable()
    {
        var tex = await TexAsync(Mapped("chapter-04") + FormattedTable);

        tex.ShouldContain("\\textbf{Sphere of Earth}");
        tex.ShouldContain("\\emph{slanted} & \\textbf{heavy} and \\emph{light}");
    }

    [TestCase("chapter-03")]
    [TestCase("chapter-04")]
    public async Task Tex_WhenAMappedChapterIsPrinted_ShouldNotPrintAnyUnfencedTextAsLiteralLatex(string chapter)
    {
        var tex = await TexAsync(Mapped(chapter));

        tex.ShouldNotContain("\\textbackslash");
        tex.ShouldNotContain("newpage");
    }

    // The tex tests above never prove the tables compile: this one typesets chapter 4 (without its figure fences) to a PDF.
    [Test]
    public async Task Compile_WhenTheMappedChapterHasPipeTables_ShouldTypesetAPdf()
    {
        var body = FigureFence().Replace(Mapped("chapter-04"), string.Empty) + FormattedTable;

        var pdf = await RenderAsync(body);

        body.ShouldNotContain("includegraphics");
        pdf.Error.ShouldBeNull(pdf.Log);
        (await File.ReadAllBytesAsync(pdf.PdfPath!)).Take(5).ShouldBe("%PDF-"u8.ToArray());
    }
}
