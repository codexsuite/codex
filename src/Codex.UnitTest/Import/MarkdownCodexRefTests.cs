using System.Text;
using Codex.Application.Features.Import;

namespace Codex.UnitTest.Import;

// A manuscript folder whose scenes and chapters name their knowledge base entries by path (issue #79).
[TestFixture]
public class MarkdownCodexRefTests
{
    private const string Ch14 = "pe/manuscript/act-02-b/chapter-14-first";
    private const string Ch15 = "pe/manuscript/act-02-b/chapter-15-second";

    private static ImportFile File(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static List<ImportFile> Book(params ImportFile[] extra) =>
    [
        File("pe/manuscript/index.md", "---\ntitle: Product Engineering\n---\n"),
        File("pe/manuscript/act-02-b/index.md", "---\ntitle: Act B\n---\n"),
        File(Ch14 + "/index.md", "---\ntitle: Fourteen\ncodex:\n  - name: Goodhart's Law\n    path: ../../../other/goodharts-law/entry.md\n  - Feature factory\n---\n"),
        File(Ch15 + "/index.md", "---\ntitle: Fifteen\n---\n"),
        .. extra,
    ];

    private static ImportFile Goodhart() => File("pe/other/goodharts-law/entry.md",
        "---\nid: 0ujsswThIGTUYm2K8FjOOfXtY1K\nname: Goodhart's Law\ntags: [Laws, story]\naliases: [story.Goodhart, Goodhart]\nalwaysIncludeInContext: true\nsummary: A measure that is a target stops being a measure.\n---\n\nWhen a measure becomes a target.\n");

    private static ImportPlan Parse(params ImportFile[] extra) => new MarkdownFolderParser().Parse(new ImportUpload("markdown", Book(extra)));

    [Test]
    public void Parse_WhenChapterListNamesAFileInTheUpload_ShouldMakeAnEntryFromItsTextAndNovelcrafterMetadata()
    {
        var plan = Parse(Goodhart());

        var entry = plan.Entries.Single(e => e.Name == "Goodhart's Law");
        entry.Key.ShouldBe("path:pe/other/goodharts-law/entry.md");
        entry.Category.ShouldBe("Laws");
        entry.Body.ShouldBe("When a measure becomes a target.\n");
        entry.Summary.ShouldBe("A measure that is a target stops being a measure.");
        entry.Aliases.ShouldBe(["story.Goodhart", "Goodhart"]);
        entry.AlwaysInContext.ShouldBeTrue();
        entry.ExternalId.ShouldBe("0ujsswThIGTUYm2K8FjOOfXtY1K");
    }

    [Test]
    public void Parse_WhenChapterListsEntries_ShouldCreateThemWithoutLinkingScenes()
    {
        var plan = Parse(Goodhart(), File(Ch14 + "/scene-01.md", "---\nid: a\n---\n"));

        plan.Entries.Select(e => e.Name).ShouldBe(["Goodhart's Law", "Feature factory"]);
        plan.Acts[0].Chapters[0].Scenes[0].Entries.ShouldBeEmpty();
    }

    [Test]
    public void Parse_WhenSceneItemHasPathAndUrl_ShouldUseTheFileAsBodyKeepTheUrlAndLinkTheScene()
    {
        var plan = Parse(
            Goodhart(),
            File("pe/blog/post-one/post.md", "---\ntitle: Post one\n---\n\n# Post one\n\nThe blog text.\n"),
            File(Ch14 + "/scene-01.md", "---\nid: a\ncodex:\n  - name: Post one\n    path: ../../../blog/post-one/post.md\n    url: https://example.com/one\n---\n"));

        var entry = plan.Entries.Single(e => e.Name == "Post one");
        entry.Category.ShouldBe(MarkdownFolderParser.ImportedCategory);
        entry.Body.ShouldBe("The blog text.\n");
        entry.ImportUrl.ShouldBe("https://example.com/one");
        plan.Acts[0].Chapters[0].Scenes[0].Entries.ShouldBe([entry.Key]);
        plan.Skipped.ShouldBeEmpty();
    }

    [Test]
    public void Parse_WhenTwoFilesShareAName_ShouldMakeTwoEntriesToldApartByChapter()
    {
        var item = "---\nid: {0}\ncodex:\n  - name: Scene 1 voice notes\n    path: scene-01-notes.md\n---\n";
        var plan = Parse(
            File(Ch14 + "/scene-01-notes.md", "Fourteen's notes.\n"),
            File(Ch15 + "/scene-01-notes.md", "Fifteen's notes.\n"),
            File(Ch14 + "/scene-01.md", string.Format(item, "a")),
            File(Ch15 + "/scene-01.md", string.Format(item, "b")));

        var notes = plan.Entries.Where(e => e.Body != null && e.Body.Contains("notes")).ToList();
        notes.Select(e => e.Name).ShouldBe(["Scene 1 voice notes (Fourteen)", "Scene 1 voice notes (Fifteen)"]);
        notes.Select(e => e.Body).ShouldBe(["Fourteen's notes.\n", "Fifteen's notes.\n"]);
        var scenes = plan.Acts[0].Chapters.SelectMany(c => c.Scenes).ToList();
        scenes[0].Entries.ShouldBe([notes[0].Key]);
        scenes[1].Entries.ShouldBe([notes[1].Key]);
    }

    [Test]
    public void Parse_WhenTwoScenesNameOneFileWithDifferentSpelling_ShouldShareOneEntry()
    {
        var plan = Parse(
            Goodhart(),
            File(Ch14 + "/scene-01.md", "---\nid: a\ncodex:\n  - path: ../../../OTHER/goodharts-law/Entry.md\n---\n"),
            File(Ch15 + "/scene-01.md", "---\nid: b\ncodex:\n  - name: Goodhart's Law\n    path: ../../../other/goodharts-law/entry.md\n---\n"));

        plan.Entries.Count(e => e.Name == "Goodhart's Law").ShouldBe(1);
        plan.Acts[0].Chapters.SelectMany(c => c.Scenes).Select(s => s.Entries.Single()).Distinct().Count().ShouldBe(1);
    }

    [Test]
    public void Parse_WhenPlainNameMatchesAnEntryAPathBroughtIn_ShouldLinkThatEntry()
    {
        var plan = Parse(Goodhart(), File(Ch15 + "/scene-01.md", "---\nid: b\ncodex: [goodhart's law]\n---\n"));

        plan.Entries.Count(e => e.Name.Equals("Goodhart's Law", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        plan.Acts[0].Chapters[1].Scenes[0].Entries.ShouldBe(["path:pe/other/goodharts-law/entry.md"]);
    }

    [Test]
    public void Parse_WhenPathIsNotInTheUpload_ShouldKeepANameOnlyEntryAndSayWhy()
    {
        var plan = Parse(File(Ch14 + "/scene-01.md", "---\nid: a\ncodex:\n  - name: Gone\n    path: ../../../../../etc/passwd\n    url: https://example.com/gone\n---\n"));

        var gone = plan.Entries.Single(e => e.Name == "Gone");
        (gone.Body, gone.Category, gone.ImportUrl).ShouldBe((null, "Imported", "https://example.com/gone"));
        plan.Skipped.Select(s => s.Reason).ShouldContain(r => r.StartsWith("2 codex files named by path are not in the upload", StringComparison.Ordinal));
    }

    [Test]
    public void Parse_WhenFileIsTaggedNothing_ShouldFallBackToImportedAndStopListingItAsSkipped()
    {
        var plan = Parse(Goodhart(), File("pe/other/plain/entry.md", "Just text.\n"),
            File(Ch15 + "/scene-01.md", "---\nid: b\ncodex:\n  - path: ../../../other/plain/entry.md\n---\n"));

        var plain = plan.Entries.Single(e => e.Key.EndsWith("plain/entry.md", StringComparison.Ordinal));
        (plain.Name, plain.Category, plain.Body).ShouldBe(("Plain", "Imported", "Just text.\n"));
        plan.Skipped.Select(s => s.Path).ShouldNotContain("pe/other/plain/entry.md");
        plan.Skipped.Select(s => s.Path).ShouldNotContain("pe/other/goodharts-law/entry.md");
    }

    [Test]
    public void Parse_WhenSceneListIsPlainNames_ShouldKeyByLowerCasedNameAsBefore()
    {
        var plan = Parse(File(Ch14 + "/scene-01.md", "---\nid: a\ncodex: [Mara, mara]\n---\n"));

        plan.Entries.Single(e => e.Name == "Mara").Key.ShouldBe("mara");
        plan.Acts[0].Chapters[0].Scenes[0].Entries.ShouldBe(["mara"]);
    }
}
