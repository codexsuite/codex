using Codex.Application.Features.Import;
using Codex.TestSupport;

namespace Codex.UnitTest.Import;

[TestFixture]
public class MarkdownFolderParserTests
{
    private static ImportUpload SmallFolder()
    {
        var folder = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "markdown-small");
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => new ImportFile("markdown-small/" + Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllBytes(f)))
            .ToList();
        return new ImportUpload("markdown", files);
    }

    [Test]
    public void Parse_WhenSmallFolder_ShouldReadActsInReadingOrderAndScenesInNumberOrder()
    {
        var plan = new MarkdownFolderParser().Parse(SmallFolder());

        plan.Acts.Select(a => a.Title).ShouldBe(["The Call", "The Road"]);
        plan.Acts.SelectMany(a => a.Chapters).Select(c => c.Title).ShouldBe(["Preface", "A Letter Arrives", "Crossing"]);
        plan.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Select(s => s.ImportRef).ShouldBe(["ch00-s01", "ch01-s01", "ch01-s02", "ch03-s01"]);
        plan.Entries.Select(e => e.Name).ShouldBe(["Mara", "The Lantern", "Tobin"]);
        plan.Skipped.ShouldBeEmpty();
    }

    [Test]
    public void Parse_WhenStatusUnknownAndIdRepeated_ShouldDeriveStatusAndReportBoth()
    {
        var files = new[]
        {
            new ImportFile("act-01-a/chapter-01-b/scene-01.md", "---\nid: x\nstatus: polished\n---\n\n## Scene Beats\n\n1. One.\n"u8.ToArray()),
            new ImportFile("act-01-a/chapter-01-b/scene-02.md", "---\nid: x\n---\n"u8.ToArray()),
        };

        var plan = new MarkdownFolderParser().Parse(new ImportUpload("markdown", files));

        var scenes = plan.Acts[0].Chapters[0].Scenes;
        scenes.Select(s => (s.Status, s.ImportRef)).ShouldBe([("beats", "x"), ("outline", null)]);
        plan.Skipped.Select(s => s.Path).ShouldBe(["act-01-a/chapter-01-b/scene-01.md", "act-01-a/chapter-01-b/scene-02.md"]);
        plan.Book.Title.ShouldBe("Imported book");
    }

    [Test]
    public void Parse_WhenSceneProseIsPlainMarkdownWithWindowsPaths_ShouldKeepItAsTextAndListNothing()
    {
        var files = new[]
        {
            new ImportFile("act-01-a/chapter-01-b/scene-01.md", "---\nid: x\n---\n"u8.ToArray()),
            new ImportFile("act-01-a/chapter-01-b/scene-01-prose.md", "Saved to C:\\Users\\joel\\notes.txt and \\o/.\n"u8.ToArray()),
        };

        var plan = new MarkdownFolderParser().Parse(new ImportUpload("markdown", files));

        plan.Skipped.ShouldBeEmpty();
        plan.Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe("Saved to C:\\\\Users\\\\joel\\\\notes.txt and \\\\o/.\n");
    }

    [Test]
    public void Parse_WhenSceneProseHoldsLatex_ShouldMapItAndListWhatWasKept()
    {
        var prose = "\\begin{DndReadAloud}\nA bell tolls.\n\\end{DndReadAloud}\n\n\\newpage\n\nRoll \\DndDice{1d6}.\n";
        var files = new[]
        {
            new ImportFile("act-01-a/chapter-01-b/scene-01.md", "---\nid: x\n---\n"u8.ToArray()),
            new ImportFile("act-01-a/chapter-01-b/scene-01-prose.md", System.Text.Encoding.UTF8.GetBytes(prose)),
        };

        var plan = new MarkdownFolderParser().Parse(new ImportUpload("markdown", files));

        plan.Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe(":::readaloud\nA bell tolls.\n:::\n\n::pagebreak\n\nRoll `\\DndDice{1d6}`{=latex}.\n");
        plan.Skipped.ShouldHaveSingleItem().Path.ShouldBe("act-01-a/chapter-01-b/scene-01-prose.md");
    }
}
