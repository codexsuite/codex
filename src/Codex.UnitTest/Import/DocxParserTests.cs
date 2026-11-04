using Codex.Application.Features.Import;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;

namespace Codex.UnitTest.Import;

[TestFixture]
public class DocxParserTests
{
    private static ImportPlan Parse(byte[] docx, string name = "product-engineering.docx") =>
        new DocxParser().Parse(new ImportUpload("docx", [new ImportFile(name, docx)]));

    [Test]
    public void Parse_WhenProductEngineeringDocument_ShouldMapHeadingsToChaptersAndScenes()
    {
        var plan = Parse(ProductEngineeringV1.Docx());

        plan.Book.Title.ShouldBe("Product Engineering");
        plan.Book.Subtitle.ShouldBe("Shipping the right thing");
        var act = plan.Acts.ShouldHaveSingleItem();
        act.Title.ShouldBe("Act 1");
        act.Chapters.Select(c => (c.Title, c.IsNumbered)).ShouldBe([
            ("Front matter", false), ("Preface", true), ("A Brief History of Software Practice", true), ("Owning Outcomes", true),
        ]);
        act.Chapters[2].Scenes.Select(s => s.Title).ShouldBe(["The beginnings in Agile", "Feature factories"]);
        act.Chapters[1].Scenes.ShouldHaveSingleItem().Status.ShouldBe("draft");
    }

    [Test]
    public void Parse_WhenRunsAreItalicOrBold_ShouldKeepEmphasisInStoredProse()
    {
        var preface = Parse(ProductEngineeringV1.Docx()).Acts[0].Chapters[1].Scenes[0];

        preface.Prose.ShouldBe(
            "For ten years I watched good teams ship the wrong thing, *on time*.\n\nThis book is about the **other** way to work.\n");
    }

    [Test]
    public void Parse_WhenSectionBreakFallsInAChapter_ShouldStartANewScene()
    {
        var chapter = Parse(ProductEngineeringV1.Docx()).Acts[0].Chapters[3];

        chapter.Scenes.Select(s => (s.Title, s.Prose)).ShouldBe([
            (null, "Engineers belong in discovery.\n"),
            (null, "When a measure becomes a target, it ceases to be a good measure.\n"),
        ]);
    }

    [Test]
    public void Parse_WhenDocumentHasImagesAndComments_ShouldListThemAsSkipped()
    {
        var plan = Parse(ProductEngineeringV1.Docx());

        plan.Skipped.Select(s => (s.Path, s.Reason)).ShouldBe([("Images", "1 image is not imported"), ("Comments", "1 comment is not imported")]);
    }

    [Test]
    public void Parse_WhenTextLooksLikeMarkdown_ShouldStoreItAsTyped()
    {
        var docx = new DocxBuilder()
            .Heading(1, "One")
            .Paragraph("# not a heading")
            .Paragraph("- not a list")
            .Paragraph("2*3*4 and snake_case and [brackets]")
            .Paragraph("::beat{id=\"x\"}")
            .Paragraph("---")
            .Build();

        var prose = Parse(docx).Acts[0].Chapters[0].Scenes[0].Prose;

        prose.ShouldBe("\\# not a heading\n\n\\- not a list\n\n2\\*3\\*4 and snake\\_case and \\[brackets\\]\n\n\\::beat{id=\"x\"}\n\n\\* \\* \\*\n");
    }

    [Test]
    public void Parse_WhenNoHeading1_ShouldRefuseAndSayWhy()
    {
        var docx = new DocxBuilder().Title("Notes").Heading(2, "Only a scene").Paragraph("Text.").Build();

        Should.Throw<ApiException>(() => Parse(docx)).Message.ShouldContain("no Heading 1");
    }

    [Test]
    public void Parse_WhenTitleParagraphMissing_ShouldTakeTheBookTitleFromTheFileName()
    {
        var plan = Parse(new DocxBuilder().Heading(1, "One").Paragraph("Text.").Build(), "field-notes.docx");

        plan.Book.Title.ShouldBe("field notes");
        plan.FileName.ShouldBe("field-notes.docx");
    }

    [Test]
    public void Parse_WhenNotADocx_ShouldRefuse()
    {
        Should.Throw<ApiException>(() => Parse("plain text"u8.ToArray())).Message.ShouldContain("not a readable");
        Should.Throw<ApiException>(() => Parse(TestZip.Of(("readme.txt", "x")))).Message.ShouldContain("no main document part");
        Should.Throw<ApiException>(() => Parse([], "book.zip")).Message.ShouldContain(".docx");
    }

    [Test]
    public void Parse_WhenPackageExpandsFarBeyondItsSize_ShouldRefuse()
    {
        var zeros = new string('0', 4 * 1024 * 1024);
        var bomb = TestZip.Of(("word/document.xml", "<w:document/>"), ("word/media/a.xml", zeros), ("word/media/b.xml", zeros));

        Should.Throw<ApiException>(() => Parse(bomb)).Message.ShouldContain("expands far beyond");
    }

    [Test]
    public void Parse_WhenEntryNameEscapesThePackage_ShouldRefuse()
    {
        var zip = TestZip.Of(("word/document.xml", "<w:document/>"), ("../evil.xml", "x"));

        Should.Throw<ApiException>(() => Parse(zip)).Message.ShouldContain("not a path inside it");
    }

    [Test]
    public void Parse_WhenItalicsTouchPunctuationOrSitInsideAWord_ShouldKeepTheEmphasis()
    {
        var docx = new DocxBuilder()
            .Heading(1, "One")
            .Runs(("Ship It Friday", false, true), ("\u2014the book", false, false))
            .Runs(("un", false, false), ("believ", false, true), ("able", false, false))
            .Build();

        var prose = Parse(docx).Acts[0].Chapters[0].Scenes[0].Prose;

        prose.ShouldBe("*Ship It Friday*\u2014the book\n\nun*believ*able\n");
    }

    [Test]
    public void Parse_WhenTwoMarkedRunsTouch_ShouldKeepTheTextAndDropTheMarkThatCannotPair()
    {
        var docx = new DocxBuilder().Heading(1, "One").Runs(("a", false, true), ("b", true, false)).Build();

        Parse(docx).Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe("*a*b\n");
    }

    [Test]
    public void Parse_WhenParagraphStartsWithNumberAndParen_ShouldNotBecomeAList()
    {
        var docx = new DocxBuilder().Heading(1, "One").Paragraph("1) first").Paragraph("2. second").Build();

        Parse(docx).Acts[0].Chapters[0].Scenes[0].Prose.ShouldBe("1) first\n\n2\\. second\n");
    }

    [Test]
    public void Parse_WhenPartsNestThousandsDeep_ShouldRefuseWithoutLoadingThem()
    {
        var deep = DocxBuilder.DeeplyNested(20_000);

        deep.Length.ShouldBeLessThan(16 * 1024);
        Should.Throw<ApiException>(() => Parse(deep)).Message.ShouldContain("nested more than 256 levels");
    }

    [Test]
    public void Parse_WhenMainPartIsNotNamedDocumentXml_ShouldFollowThePackageRelationship()
    {
        var body = "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Only chapter</w:t></w:r></w:p>";
        var xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "<w:p><w:r><w:t>Text.</w:t></w:r></w:p></w:body></w:document>";

        var plan = Parse(DocxBuilder.PackageOf(xml, "word/main2.xml"));

        plan.Acts[0].Chapters.Select(c => c.Title).ShouldBe(["Only chapter"]);
    }

    [Test]
    public void Parse_WhenMainPartIsOverTheCap_ShouldRefuse()
    {
        var random = new Random(1);
        var noise = new byte[16 * 1024 * 1024];
        random.NextBytes(noise);
        var text = Convert.ToBase64String(noise);
        var xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:body></w:document>";

        Should.Throw<ApiException>(() => Parse(DocxBuilder.PackageOf(xml))).Message.ShouldContain("more than an import can read");
    }

    [Test]
    public void Parse_WhenASceneIsOverTheProseLimit_ShouldImportItEmptyAndListItAsTooLarge()
    {
        var random = new Random(2);
        var half = string.Concat(Enumerable.Range(0, (ImportLimits.MaxFileBytes / 2) + 1).Select(_ => (char)('a' + random.Next(26))));
        var docx = new DocxBuilder().Heading(1, "Big").Paragraph(half).Paragraph(half).Heading(1, "Small").Paragraph("Fine.").Build();

        var plan = Parse(docx);

        var chapters = plan.Acts[0].Chapters;
        chapters[0].Scenes.ShouldHaveSingleItem().Prose.ShouldBeNull();
        chapters[1].Scenes.ShouldHaveSingleItem().Prose.ShouldBe("Fine.\n");
        plan.Skipped.ShouldBe([new ImportSkippedItem("Big, scene 1", ImportLimits.TooLarge)]);
    }
}
