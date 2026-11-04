using System.Text;
using Codex.Application.Features.Import;

namespace Codex.UnitTest.Import;

// The dndbook LaTeX the pandoc source maps, case by case (the Players Guide excerpts are in PandocFolderParserTests).
[TestFixture]
public class PandocChapterReaderTests
{
    private static ImportPlan Chapter(string text) =>
        new PandocFolderParser().Parse(new ImportUpload("pandoc", [new ImportFile("b/Chapter 1.md", Encoding.UTF8.GetBytes(text))]));

    private static string Prose(string text) => Chapter(text).Acts.SelectMany(a => a.Chapters).Single().Scenes.Single().Prose!;

    [TestCase("3.1 Fighter", "Fighter")]
    [TestCase("3.1.1 Overview", "Overview")]
    [TestCase("4.6.1. Spells", "Spells")]
    [TestCase("2. Races", "Races")]
    [TestCase("Paladins in Velmara", "Paladins in Velmara")]
    [TestCase("1984 Was a Year", "1984 Was a Year")]
    [TestCase("3.1", "3.1")]
    [TestCase("1.2 Title {#sec:title}", "Title")]
    public void StripNumber_WhenHeadingIsNumberedByHand_ShouldDropTheNumber(string heading, string expected)
    {
        PandocChapterReader.StripNumber(heading).ShouldBe(expected);
    }

    [Test]
    public void Parse_WhenDndBoxes_ShouldMakeContainers()
    {
        var prose = Prose(
            "# A\n\n\\begin{DndReadAloud}\n  The door groans open.\n\\end{DndReadAloud}\n\n" +
            "\\begin{DndSidebar}{Running \"the\" hall}\nGive them a moment.\n\n- Torches\n\\end{DndSidebar}\n\n" +
            "\\begin{DndComment}{Table rule}\n  Roll initiative.\n\\end{DndComment}\n");

        prose.ShouldBe(
            ":::readaloud\nThe door groans open.\n:::\n\n" +
            ":::sidebar{title=\"Running 'the' hall\"}\nGive them a moment.\n\n- Torches\n:::\n\n" +
            ":::aside\n**Table rule**\n\nRoll initiative.\n:::\n");
    }

    [Test]
    public void Parse_WhenLatexIsLeftOver_ShouldFenceItsWholeParagraphOrEnvironment()
    {
        var prose = Prose(
            "# A\n\nPlain text.\n\nA \\textbf{bold} claim.\n\n\\begin{DndMonster}{Foo}\n\\DndMonsterType{Medium}\n\\end{DndMonster}\n\n\\vspace{1cm}\n");

        prose.ShouldBe(
            "Plain text.\n\n```{=latex}\nA \\textbf{bold} claim.\n```\n\n" +
            "```{=latex}\n\\begin{DndMonster}{Foo}\n\\DndMonsterType{Medium}\n\\end{DndMonster}\n```\n\n```{=latex}\n\\vspace{1cm}\n```\n");
    }

    [Test]
    public void Parse_WhenBreaksAndColumnsAreInTheText_ShouldKeepOnlyManualBreaks()
    {
        var prose = Prose("\\newpage\n# A\n\nOne line\n\\clearpage\n\\newpage\n\nshape the\n\n\\onecolumn\n\nvery fabric.\n\\twocolumn\n\\clearpage\n");

        prose.ShouldBe("One line\n\n::pagebreak\n\nshape the\n\nvery fabric.\n");
    }

    [Test]
    public void Parse_WhenACommentFollowsTextOnItsLine_ShouldKeepBoth()
    {
        var prose = Prose("# A\n\nSome text <!-- a note\nover two lines --> and more.\n");

        prose.ShouldBe("Some text\n\n:::comment\n<!-- a note\nover two lines -->\n:::\n\nand more.\n");
    }

    [Test]
    public void Parse_WhenAPartHasNoFigure_ShouldStillNameTheAct()
    {
        var plan = Chapter("\\part{The Fallen World}\n\nIntro words.\n\n# 1.1 Start\n\nText.\n\n\\part{Second}\n\n# 2.1 Next\n\nMore.\n");

        plan.Acts.Select(a => (a.Title, a.Opener)).ShouldBe([("The Fallen World", (ImportOpenerPlan?)null), ("Second", null)]);
        plan.Acts.SelectMany(a => a.Chapters).Select(c => c.Title).ShouldBe(["Start", "Next"]);
        plan.Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe("Intro words.\n\nText.\n");
    }

    [Test]
    public void Parse_WhenAFileHasNoHeading_ShouldBeOneChapterNamedAfterItsAct()
    {
        var plan = Chapter("\\begin{figure}[p]\n\\part{Appendix}\n\\end{figure}\n\nJust text.\n");

        plan.Acts.Single().Chapters.Single().Title.ShouldBe("Appendix");
    }

    [Test]
    public void Parse_WhenASpellHeaderIsInTheText_ShouldTakeTheTextUpToTheNextHeading()
    {
        var plan = Chapter(
            "# Spells\n\n\\DndSpellHeader{Ember Ward}{1st-level abjuration}{1 action}{Self}{V, S}{1 hour}\nA ward of embers.\n\nIt glows.\n\n## After\n\nProse again.\n");

        var entry = plan.Entries.ShouldHaveSingleItem();
        entry.Body.ShouldBe("A ward of embers.\n\nIt glows.\n");
        entry.Fields!["duration"].ShouldBe("1 hour");
        plan.Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe($"::spell{{kb=\"{entry.Id}\"}}\n\n# After\n\nProse again.\n");
    }

    [Test]
    public void Parse_WhenAnImageHasAttributesOrIsInsideText_ShouldPlaceAColumnFigure()
    {
        var upload = new ImportUpload("pandoc",
            [new ImportFile("b/Chapter 1.md", "# A\n\n![Wide one](images/a%20b.png){ width=600px }\n\nSee ![x](images/c.png) here.\n"u8.ToArray())],
            [new ImportSkippedItem("b/images/a b.png", ImportLimits.NotMarkdown), new ImportSkippedItem("b/images/C.PNG", ImportLimits.NotMarkdown)]);

        var plan = new PandocFolderParser().Parse(upload);

        var scene = plan.Acts[0].Chapters[0].Scenes[0];
        scene.Figures!.Select(f => (f.ImagePath, f.Placement, f.Caption)).ShouldBe([("b/images/a b.png", "column", "Wide one"), ("b/images/C.PNG", "column", "x")]);
        scene.Prose.ShouldBe($"::figure{{id=\"{scene.Figures![0].Id}\"}}\n\nSee  here.\n\n::figure{{id=\"{scene.Figures[1].Id}\"}}\n");
    }
}
