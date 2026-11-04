using Codex.Application.Features.Print.Render;
using Codex.Contracts.Errors;

namespace Codex.UnitTest.Application;

[TestFixture]
public class TexFinishTests
{
    private const string Memoir =
        "\\documentclass{memoir}\n\\setstocksize{9in}{6in}\n\\settrimmedsize{\\stockheight}{\\stockwidth}{*}\n\\checkandfixthelayout\n\\begin{document}\nx\n\\end{document}\n";

    [Test]
    public void Apply_WhenThereIsNothingToFinish_ShouldReturnTheTexUnchanged()
    {
        TexFinish.Apply(Memoir, new PrintFinish(0, false, false), "Novel").ShouldBe(Memoir);
    }

    [Test]
    public void Apply_WhenABleedIsGiven_ShouldGrowTheStockPageAndKeepTheTrim()
    {
        var tex = TexFinish.Apply(Memoir, new PrintFinish(0.125, false, false), "Novel");

        tex.ShouldContain("\\setstocksize{9.25in}{6.25in}");
        tex.ShouldContain("\\settrimmedsize{9in}{6in}{*}");
        tex.ShouldContain("\\settrims{0.125in}{0.125in}");
        tex.ShouldNotContain("trimLmarks");
    }

    [Test]
    public void Apply_WhenCropMarksAreWanted_ShouldAddThemBeforeTheDocumentBody()
    {
        var tex = TexFinish.Apply(Memoir, new PrintFinish(0.125, true, false), "Novel");

        tex.IndexOf("\\trimLmarks", StringComparison.Ordinal).ShouldBeLessThan(tex.IndexOf("\\begin{document}", StringComparison.Ordinal));
    }

    [Test]
    public void Apply_WhenADraftIsExported_ShouldPutTheWatermarkHookBeforeTheDocumentBody()
    {
        var tex = TexFinish.Apply(Memoir, new PrintFinish(0, false, true), "Novel");

        tex.IndexOf("DRAFT", StringComparison.Ordinal).ShouldBeLessThan(tex.IndexOf("\\begin{document}", StringComparison.Ordinal));
        tex.ShouldContain("shipout/background");
    }

    [Test]
    public void Apply_WhenTheTemplateSetsNoStockSize_ShouldRefuseABleed()
    {
        var plain = "\\documentclass{book}\n\\begin{document}\nx\n\\end{document}\n";

        Should.Throw<ApiException>(() => TexFinish.Apply(plain, new PrintFinish(0.125, false, false), "Manuscript"))
            .Message.ShouldContain("bleed");
    }

    [Test]
    public void Apply_WhenCropMarksComeWithoutABleed_ShouldRefuse()
    {
        Should.Throw<ApiException>(() => TexFinish.Apply(Memoir, new PrintFinish(0, true, false), "Novel"))
            .Message.ShouldContain("Crop marks");
    }
}
