using Codex.Application.Features.Import;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;

namespace Codex.UnitTest.Import;

[TestFixture]
public class NovelcrafterZipParserTests
{
    private static ImportUpload Upload(byte[] zip, string name = "product-engineering-v1.zip") =>
        new("novelcrafter", [new ImportFile(name, zip)]);

    private static ImportPlan Plan() => new NovelcrafterZipParser().Parse(Upload(ProductEngineeringV1.NovelcrafterZip()));

    [Test]
    public void Parse_WhenProductEngineeringExport_ShouldReadTheManuscriptLayout()
    {
        var plan = Plan();

        plan.FileName.ShouldBe("product-engineering-v1.zip");
        plan.Book.Title.ShouldBe("Product Engineering");
        plan.Book.Subtitle.ShouldBe("Shipping the right thing");
        plan.Book.StyleGuide.ShouldBe("# Writing rules\n\nPlain words. Evidence before anecdote. Name the team, never the person.");
        plan.Acts.Select(a => a.Title).ShouldBe(["Introduction", "The Product Engineer"]);
        plan.Acts.SelectMany(a => a.Chapters).Select(c => (c.Title, c.IsNumbered))
            .ShouldBe([("Preface", false), ("A Brief History of Software Practice", true), ("Owning Outcomes", true)]);
        plan.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Select(s => s.ImportRef).ShouldBe(["ch01-s01", "ch02-s01", "ch02-s02", "ch03-s01"]);
    }

    [Test]
    public void Parse_WhenCodexFilesHaveKsuids_ShouldKeepThemAsExternalIdsWithTheFolderAsCategory()
    {
        var plan = Plan();

        plan.Entries.Where(e => e.ExternalId != null).Select(e => (e.Category, e.Name, e.ExternalId))
            .ShouldBe([
                ("Characters", "Kent Beck", ProductEngineeringV1.BeckKsuid),
                ("Other", "Feature Factory", ProductEngineeringV1.FeatureFactoryKsuid),
                ("Other", "Goodhart's Law", ProductEngineeringV1.GoodhartKsuid),
            ], ignoreOrder: true);
        var beck = plan.Entries.Single(e => e.ExternalId == ProductEngineeringV1.BeckKsuid);
        beck.Aliases.ShouldBe(["Beck", "KB"]);
        beck.Summary.ShouldBe("Creator of Extreme Programming.");
        beck.Body.ShouldBe("Wrote *Extreme Programming Explained* and co-signed the Agile Manifesto.\n");
    }

    [Test]
    public void Parse_WhenSceneNamesAnEntryByKsuidOrNameOrAlias_ShouldLinkTheRealEntry()
    {
        var plan = Plan();

        var scenes = plan.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).ToList();
        string NameOf(string key) => plan.Entries.Single(e => e.Key == key).Name;
        scenes[1].Entries.Select(NameOf).ShouldBe(["Kent Beck", "Agile Manifesto"]);
        scenes[2].Entries.Select(NameOf).ShouldBe(["Goodhart's Law", "Feature Factory"]);
        // A name that is not in the Codex still comes across, in Imported, as the Markdown folder does.
        plan.Entries.Single(e => e.Name == "Agile Manifesto").Category.ShouldBe(MarkdownFolderParser.ImportedCategory);
        plan.Entries.Count.ShouldBe(4);
    }

    [Test]
    public void Parse_WhenSceneNamesAKsuidNotInTheExport_ShouldLeaveTheLinkOutAndSayWhy()
    {
        var zip = TestZip.Of(
            ("act-01-a/chapter-01-b/scene-01.md", "---\ncodex: [0ujsswThIGTUYm2K8FjOOfXtY1K, Mara]\n---\n"));

        var plan = new NovelcrafterZipParser().Parse(Upload(zip, "x.zip"));

        plan.Entries.Select(e => e.Name).ShouldBe(["Mara"]);
        plan.Skipped.ShouldContain(s => s.Path == "Scene codex links" && s.Reason.Contains("\"0ujsswThIGTUYm2K8FjOOfXtY1K\""));
    }

    [Test]
    public void Parse_WhenZipHoldsOtherFiles_ShouldListThemAsSkippedUnread()
    {
        var plan = Plan();

        plan.Skipped.Select(s => (s.Path, s.Reason)).ShouldBe([("product-engineering-v1/research/sources.txt", ImportLimits.NotMarkdown)]);
        plan.FileCount.ShouldBe(18);
    }

    [Test]
    public void Parse_WhenEntryIsOverTheFileCap_ShouldSkipItWithoutReadingIt()
    {
        var big = new string('a', ImportLimits.MaxFileBytes + 1);
        var zip = TestZip.Of(("act-01-a/index.md", "---\ntitle: A\n---\n"), ("act-01-a/chapter-01-b/scene-01-prose.md", big));

        var plan = new NovelcrafterZipParser().Parse(Upload(zip, "x.zip"));

        plan.Skipped.ShouldContain(new ImportSkippedItem("act-01-a/chapter-01-b/scene-01-prose.md", ImportLimits.TooLarge));
    }

    [Test]
    public void Parse_WhenNotAZip_ShouldRefuse()
    {
        var e = Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload("not a zip at all"u8.ToArray())));

        e.Message.ShouldContain("not a readable");
    }

    [TestCase("../evil.md")]
    [TestCase("/etc/passwd.md")]
    [TestCase("a/../../evil.md")]
    [TestCase("a\\..\\evil.md")]
    [TestCase("C:/evil.md")]
    public void Parse_WhenEntryNameEscapesTheArchive_ShouldRefuseTheWholeUpload(string name)
    {
        var zip = TestZip.Of((name, "x"), ("act-01-a/index.md", "---\ntitle: A\n---\n"));

        var e = Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip)));

        e.Message.ShouldContain("not a path inside it");
    }

    [Test]
    public void Parse_WhenZipHasNoActFolders_ShouldRefuse()
    {
        var zip = TestZip.Of(("notes/readme.md", "hello"));

        Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip))).Message.ShouldContain("no manuscript");
    }

    [Test]
    public void Parse_WhenArchiveExpandsFarBeyondItsSize_ShouldRefuseBeforeReadingIt()
    {
        var zeros = new string('0', ImportLimits.MaxFileBytes - 10);
        var zip = TestZip.Of(Enumerable.Range(1, 6).Select(n => ($"act-01-a/chapter-01-b/scene-{n:00}.md", zeros)).ToArray());

        Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip))).Message.ShouldContain("expands far beyond");
    }

    [Test]
    public void Parse_WhenMarkdownExpandsPastTheTotalCap_ShouldRefuse()
    {
        var zeros = new string('0', ImportLimits.MaxFileBytes - 10);
        var zip = TestZip.Of(Enumerable.Range(1, 14).Select(n => ($"act-01-a/chapter-01-b/scene-{n:00}.md", zeros)).ToArray());

        Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip))).Message.ShouldContain("more than 25 MB");
    }

    [Test]
    public void Parse_WhenMoreEntriesThanTheFileCap_ShouldRefuse()
    {
        var zip = TestZip.Of(Enumerable.Range(0, ImportLimits.MaxFiles + 1).Select(n => ($"f{n}.txt", "")).ToArray());

        Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip))).Message.ShouldContain("at most 2000 files");
    }

    [TestCase(10u)]
    [TestCase(5000u)]
    public void Parse_WhenAnEntryDoesNotHaveTheSizeItDeclares_ShouldRefuseInsteadOfTruncating(uint declared)
    {
        var zip = TestZip.WithDeclaredSize(TestZip.Of(("act-01-a/chapter-01-b/scene-01.md", new string('x', 200))), declared);

        Should.Throw<ApiException>(() => new NovelcrafterZipParser().Parse(Upload(zip))).Message.ShouldContain("not the size the archive says");
    }

    [Test]
    public void Parse_WhenSentSeveralFilesOrTheWrongExtension_ShouldRefuse()
    {
        var parser = new NovelcrafterZipParser();
        var two = new ImportUpload("novelcrafter", [new ImportFile("a.zip", []), new ImportFile("b.zip", [])]);

        Should.Throw<ApiException>(() => parser.Parse(two));
        Should.Throw<ApiException>(() => parser.Parse(Upload([], "book.docx")));
    }
}
