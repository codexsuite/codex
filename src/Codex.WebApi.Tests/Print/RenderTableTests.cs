using Codex.Application.Features.Print.Render;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// #13 / #64 §1: a pipe table in a scene prints with every built-in template (fantasy-rpg draws a DndTable, the others
// pandoc's longtable) and shows as an HTML table in Quick preview, with its cells' inline formatting. Needs pandoc,
// tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class RenderTableTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _chapterId;

    private IRenderService Renderer => Host.Services.GetRequiredService<IRenderService>();

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        Guid sceneId;
        (_bookId, _chapterId, sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, sceneId, $"""
            Fighters share an unparalleled **mastery** with weapons.

            | Level | Proficiency Bonus | Features |
            |:-:|--:|---|
            | 1st | +2 | **Fighting Style**, *Second Wind* |
            | 2nd | +2 | Action Surge & `code_x`, see [Chapter](chapter:{_chapterId}) |

            The end.

            """);
    }

    private RenderRequest Chapter(string template) => new(_bookId, RenderScope.Chapter, _chapterId, template);

    [TestCase("fantasy-rpg", "\\begin{DndTable}{Y>{\\raggedleft\\arraybackslash}XX}")]
    [TestCase("novel-6x9", "\\begin{longtable}")]
    [TestCase("manuscript", "\\begin{longtable}")]
    public async Task RenderPdf_WhenASceneHoldsAPipeTable_ShouldTypesetItWithTheTemplate(string template, string table)
    {
        using var pdf = await Renderer.RenderPdfAsync(Chapter(template), null, CancellationToken.None);

        pdf.Error.ShouldBeNull(pdf.Log);
        (await File.ReadAllBytesAsync(pdf.PdfPath!)).Take(5).ShouldBe("%PDF-"u8.ToArray());
        pdf.Document.Flags.ShouldBeEmpty();
        var tex = await File.ReadAllTextAsync(Path.Combine(pdf.JobDirectory, "main.tex"));
        tex.ShouldContain(table);
        tex.ShouldContain("\\textbf{Fighting Style}, \\emph{Second Wind}");
        tex.ShouldContain("\\texttt{code\\_x}");
    }

    [Test]
    public async Task RenderHtml_WhenASceneHoldsAPipeTable_ShouldGiveAnHtmlTableKeepingTheCellsFormatting()
    {
        var html = await Renderer.RenderHtmlAsync(Chapter("fantasy-rpg"), CancellationToken.None);

        html.Html.ShouldContain("<table>");
        html.Html.ShouldContain("<th style=\"text-align: center;\">Level</th>");
        html.Html.ShouldContain("<td><strong>Fighting Style</strong>, <em>Second Wind</em></td>");
        html.Html.ShouldNotContain("| Level |");
    }
}
