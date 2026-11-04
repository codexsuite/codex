using Codex.Application.Features.Import;

namespace Codex.UnitTest.Import;

[TestFixture]
public class LatexImportMapperTests
{
    private const string SpellId = "0199c0de-0000-7000-8000-000b00000005";

    [Test]
    public void Map_WhenReadAloud_ShouldBecomeAContainerHoldingTheInnerMarkdown()
    {
        var result = LatexImportMapper.Map("Before.\n\n\\begin{DndReadAloud}\n  You *hear* a bell.\n\n  It tolls.\n\\end{DndReadAloud}\n\nAfter.\n");

        result.Prose.ShouldBe("Before.\n\n:::readaloud\nYou *hear* a bell.\n\nIt tolls.\n:::\n\nAfter.\n");
        result.Mapped[LatexImportRule.ReadAloud].ShouldBe(1);
        result.Fenced.ShouldBeEmpty();
    }

    [Test]
    public void Map_WhenSidebar_ShouldKeepTheTitleAsAnAttribute()
    {
        var result = LatexImportMapper.Map("\\begin{DndSidebar}[float*]{Running the \"hall\"}\n- One\n- Two\n\\end{DndSidebar}\n");

        result.Prose.ShouldBe(":::sidebar{title=\"Running the 'hall'\"}\n- One\n- Two\n:::\n");
        result.Mapped[LatexImportRule.Sidebar].ShouldBe(1);
    }

    [Test]
    public void Map_WhenComment_ShouldBecomeAnAsideWithItsTitleAsABoldLine()
    {
        var result = LatexImportMapper.Map("\\begin{DndComment}{A Note}\nSmall print.\n\\end{DndComment}\n");

        result.Prose.ShouldBe(":::aside\n**A Note**\n\nSmall print.\n:::\n");
        result.Mapped[LatexImportRule.Aside].ShouldBe(1);
    }

    [Test]
    public void Map_WhenEnvironmentSitsInsideAContainer_ShouldFenceTheInnerOne()
    {
        var result = LatexImportMapper.Map("\\begin{DndSidebar}{Outer}\n\\begin{DndReadAloud}\nText\n\\end{DndReadAloud}\n\\end{DndSidebar}\n");

        result.Prose.ShouldStartWith(":::sidebar{title=\"Outer\"}\n```{=latex}\n\\begin{DndReadAloud}\n```");
        result.Mapped.ShouldNotContainKey(LatexImportRule.ReadAloud);
    }

    [Test]
    public void Map_WhenPageBreaksAreAdjacent_ShouldCollapseThemAndDropColumnCommands()
    {
        var result = LatexImportMapper.Map("One.\n\n\\clearpage\n\\onecolumn\n\\newpage\n\\twocolumn\n\nTwo.\n");

        result.Prose.ShouldBe("One.\n\n::pagebreak\n\nTwo.\n");
        result.Mapped[LatexImportRule.PageBreak].ShouldBe(2);
        result.Mapped[LatexImportRule.ColumnDropped].ShouldBe(2);
    }

    [Test]
    public void Map_WhenDropCap_ShouldJoinTheLetterToItsWord()
    {
        var result = LatexImportMapper.Map("# Fighters\n\\DndDropCapLine{F}ighters are bold.\n");

        result.Prose.ShouldBe("# Fighters\n\nFighters are bold.\n");
        result.Mapped[LatexImportRule.DropCap].ShouldBe(1);
        result.Fenced.ShouldBeEmpty();
    }

    [Test]
    public void Map_WhenHeadingIsHandNumbered_ShouldStripTheNumber()
    {
        var result = LatexImportMapper.Map("# 2.1 Introduction\n\n# 2.2 Scores\n\n## 4.6.1. Spells\n\n### 1984 and after\n\n## 3. Rules\n");

        // "3. Rules" is one level: a title that starts with a number, not numbering.
        result.Prose.ShouldBe("# Introduction\n\n# Scores\n\n## Spells\n\n### 1984 and after\n\n## 3. Rules\n");
        result.Mapped[LatexImportRule.HeadingNumber].ShouldBe(3);
    }

    [TestCase("# 1984. The year\n", "# 1984. The year\n")]
    [TestCase("# 2.5 Million Reasons\n", "# 2.5 Million Reasons\n")]
    [TestCase("# 2.5 Million Reasons\n\n# 3.1 Other\n", "# 2.5 Million Reasons\n\n# 3.1 Other\n")]
    [TestCase("## 3.1.1 Deep\n", "## Deep\n")]
    [TestCase("# 4.1 Intro\n\n# 4.2 Magic\n\n## 4.2.1 Spells\n", "# Intro\n\n# Magic\n\n## Spells\n")]
    [TestCase("# 2.1 Intro\n\n# 2.5 Million Reasons\n", "# Intro\n\n# Million Reasons\n")]
    public void Map_WhenHeadingStartsWithANumber_ShouldStripOnlyMultiLevelNumbering(string source, string expected)
    {
        LatexImportMapper.Map(source).Prose.ShouldBe(expected);
    }

    [Test]
    public void Map_WhenPlainMarkdownHasWindowsPathsAndSmileys_ShouldKeepThemAsText()
    {
        var result = LatexImportMapper.Map("Saved to C:\\Users\\joel\\notes.txt, ok \\o/.\n");

        result.Prose.ShouldBe("Saved to C:\\\\Users\\\\joel\\\\notes.txt, ok \\\\o/.\n");
        result.Fenced.ShouldBeEmpty();
    }

    [Test]
    public void Map_WhenALineStartsWithAnUnknownBackslashWord_ShouldNotFenceIt()
    {
        var result = LatexImportMapper.Map("\\Users\\joel\n");

        result.Fenced.ShouldBeEmpty();
        result.Prose.ShouldNotContain("{=latex}");
    }

    [Test]
    public void Map_WhenACommandArgumentRunsOntoTheNextLine_ShouldFenceTheWholeCommand()
    {
        var result = LatexImportMapper.Map("The \\textbf{bold\ntext} ends here.\n");

        result.Prose.ShouldBe("The `\\textbf{bold text}`{=latex} ends here.\n");
        result.Fenced.ShouldHaveSingleItem().Command.ShouldBe("textbf");
    }

    [Test]
    public void Map_WhenACommandArgumentNeverCloses_ShouldFenceOnlyTheCommand()
    {
        LatexImportMapper.Map("The \\textbf{bold\ntext ends here.\n").Prose.ShouldBe("The `\\textbf`{=latex}{bold\ntext ends here.\n");
    }

    [Test]
    public void Map_WhenAPipeLineIsNotATable_ShouldTreatItAsText()
    {
        LatexImportMapper.Map("| just a line of text\nwith *stars*\n").Prose.ShouldBe("| just a line of text\nwith *stars*\n");
    }

    [Test]
    public void Map_WhenTableCellsHoldLatex_ShouldWrapEachCommandAsASpanAndEscapeItsPipes()
    {
        var result = LatexImportMapper.Map("| Name | Note |\n|---|---|\n| \\textbf{Bold} | a \\mbox{x|y} |\n");

        result.Prose.ShouldBe("| Name | Note |\n|---|---|\n| `\\textbf{Bold}`{=latex} | a `\\mbox{x\\|y}`{=latex} |\n");
        result.Fenced.Select(f => f.Kind).ShouldBe(["span", "span"]);
    }

    [Test]
    public void Map_WhenACommentNeverCloses_ShouldEndTheBodyWithOneNewline()
    {
        LatexImportMapper.Map("Intro.\n\n<!-- parked draft\n\nReal text.\n").Prose.ShouldBe("Intro.\n\n<!-- parked draft\n\nReal text.\n");
    }

    [Test]
    public void Map_WhenAFencedRunHoldsAMarkdownFence_ShouldNotCloseTheLatexFenceEarly()
    {
        var prose = LatexImportMapper.Map("\\begin{center}{\n```\ncode\n```\n}\n").Prose;

        prose.ShouldBe("```{=latex}\n\\begin{center}{\n ```\ncode\n ```\n}\n```\n");
    }

    [Test]
    public void Map_WhenASidebarHoldsASidebar_ShouldCloseTheOuterAtItsOwnEnd()
    {
        var prose = LatexImportMapper.Map("\\begin{DndSidebar}{Outer}\n\\begin{DndSidebar}{Inner}\nIn\n\\end{DndSidebar}\nMore\n\\end{DndSidebar}\n").Prose;

        prose.ShouldStartWith(":::sidebar{title=\"Outer\"}\n```{=latex}\n\\begin{DndSidebar}{Inner}\n```");
        prose.ShouldEndWith("More\n:::\n");
    }

    [Test]
    public void Map_WhenSpellHeaderAndEntryKnown_ShouldBecomeASpellDirective()
    {
        var header = "\\DndSpellHeader\n  {Earthen Armor (Earth)}\n  {4th-level alteration}\n  {1 round}\n\nThe armor lasts.\n";

        var result = LatexImportMapper.Map(header, name => name == "Earthen Armor (Earth)" ? SpellId : null);

        result.Prose.ShouldBe($"::spell{{kb=\"{SpellId}\"}}\n\nThe armor lasts.\n");
        result.Mapped[LatexImportRule.Spell].ShouldBe(1);
    }

    [Test]
    public void Map_WhenSpellHeaderAndEntryUnknown_ShouldFenceTheWholeHeader()
    {
        var header = "\\DndSpellHeader\n  {Earthen Armor (Earth)}\n  {4th-level alteration}\n\nThe armor lasts.\n";

        var result = LatexImportMapper.Map(header);

        result.Prose.ShouldBe("```{=latex}\n\\DndSpellHeader\n  {Earthen Armor (Earth)}\n  {4th-level alteration}\n```\n\nThe armor lasts.\n");
        result.Fenced.Select(f => (f.Kind, f.Command)).ShouldBe([("block", "DndSpellHeader")]);
    }

    [Test]
    public void Map_WhenFigure_ShouldFenceItWithItsPartOpener()
    {
        var result = LatexImportMapper.Map("\\begin{figure}[p]\n\\centering\n\\part{Races}\n\\includegraphics[width=\\textwidth]{a b.png}\n\\end{figure}\n\n\\begin{figure*}[t]\n\\caption{Mage}\n\\end{figure*}\n");

        result.Prose.ShouldBe("```{=latex}\n\\begin{figure}[p]\n\\centering\n\\part{Races}\n\\includegraphics[width=\\textwidth]{a b.png}\n\\end{figure}\n```\n\n```{=latex}\n\\begin{figure*}[t]\n\\caption{Mage}\n\\end{figure*}\n```\n");
        result.Fenced.Select(f => f.Command).ShouldBe(["part+figure", "figure"]);
        result.Mapped[LatexImportRule.FigureFenced].ShouldBe(2);
    }

    [Test]
    public void Map_WhenOtherBlockCommand_ShouldFenceItsRunOfLines()
    {
        var result = LatexImportMapper.Map("Text.\n\\DndMonsterType{Medium aberration}\n\\DndMonsterBasics[\n  armor = {9},\n]\nPlain after.\n");

        result.Prose.ShouldBe("Text.\n\n```{=latex}\n\\DndMonsterType{Medium aberration}\n\\DndMonsterBasics[\n  armor = {9},\n]\n```\n\nPlain after.\n");
        result.Fenced.ShouldHaveSingleItem().Command.ShouldBe("DndMonsterType");
    }

    [Test]
    public void Map_WhenCommandSitsInsideAParagraph_ShouldKeepItAsAnInlineSpan()
    {
        var result = LatexImportMapper.Map("Roll \\DndDice{3d8 + 3} for hits, or \\emph{mage armor} first.\n\n\\emph{Mage armor} opens a line.\n");

        result.Prose.ShouldBe("Roll `\\DndDice{3d8 + 3}`{=latex} for hits, or `\\emph{mage armor}`{=latex} first.\n\n`\\emph{Mage armor}`{=latex} opens a line.\n");
        result.Fenced.Select(f => f.Kind).ShouldBe(["span", "span", "span"]);
    }

    [Test]
    public void Map_WhenCommandHoldsBackticks_ShouldUseALongerFence()
    {
        var result = LatexImportMapper.Map("See \\verb{a`b} here.\n");

        result.Prose.ShouldBe("See ``\\verb{a`b}``{=latex} here.\n");
    }

    [Test]
    public void Map_WhenHtmlCommentAndTableAndFence_ShouldKeepThemByteForByte()
    {
        var text = "<!--\n\\begin{DndReadAloud}\nx\n\\end{DndReadAloud}\n| a | b |  \n-->\n\n| Name  | Roll |  \n| ----- | ---- |\n| \\Goblin | 1d6 |\n\n```{=latex}\n\\foo{bar}\n\n\\baz\n```\n";

        LatexImportMapper.Map(text).Prose.ShouldBe(text);
    }

    [Test]
    public void Map_WhenTableFollowsAParagraphWithNoBlankLine_ShouldSeparateThem()
    {
        LatexImportMapper.Map("Intro\n| a |\n| - |\nOutro\n").Prose.ShouldBe("Intro\n\n| a |\n| - |\n\nOutro\n");
    }

    [Test]
    public void Map_WhenCrlfAndBom_ShouldProduceLfOutput()
    {
        LatexImportMapper.Map("\uFEFF# 1.1 Hello  \r\n\r\nText   \r\n\r\n# 1.2 Again\r\n").Prose.ShouldBe("# Hello\n\nText\n\n# Again\n");
    }

    [Test]
    public void Map_WhenEmpty_ShouldBeEmpty()
    {
        LatexImportMapper.Map("").Prose.ShouldBe("");
    }
}
