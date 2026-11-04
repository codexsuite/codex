using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Application.Features.Print;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;

namespace Codex.UnitTest.Application;

// The assembler's directive vocabulary, links and citations on hand-built snapshots (no database, no pandoc).
[TestFixture]
public class ManuscriptAssemblerTests
{
    private static readonly Guid Book = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Act = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Chapter1 = Guid.Parse("00000000-0000-7000-8000-000000000003");
    private static readonly Guid Chapter2 = Guid.Parse("00000000-0000-7000-8000-000000000004");
    private static readonly Guid Scene = Guid.Parse("00000000-0000-7000-8000-000000000005");
    private static readonly Guid Figure = Guid.Parse("00000000-0000-7000-8000-000000000006");
    private static readonly Guid Asset = Guid.Parse("00000000-0000-7000-8000-000000000007");
    private static readonly Guid Use = Guid.Parse("00000000-0000-7000-8000-000000000008");
    private static readonly Guid Entry = Guid.Parse("00000000-0000-7000-8000-000000000009");

    private static TemplatePackage Template(bool rawLatex = true, CitationForm citations = CitationForm.Footnote) => new(
        "test", "Test", "1.0.0", "tectonic", "/templates/test", ["us-letter"], PageLayout.Default, citations, rawLatex,
        [], new Dictionary<string, TemplateOption>(), new Dictionary<string, string>(), [], true, [], []);

    private static ManuscriptSnapshot Snapshot(string body, bool withImage = true, string fields = "{\"range\": \"Self\", \"casting_time\": \"1 action\"}", string storageKey = "assets/map/print") => new(
        new RenderBook(Book, "The Book", null, "J. Dickson"),
        RenderScope.Chapter,
        [new RenderAct(Act, "Part One", null, null)],
        [
            new RenderChapter(Chapter1, Act, "Arrival", true, 1, null, null, true),
            new RenderChapter(Chapter2, Act, "Departure", true, 2, null, null, false),
        ],
        [new RenderScene(Scene, Chapter1, body)],
        new Dictionary<Guid, RenderFigure> { [Figure] = new(Figure, Asset, "page", "The map.", "A map") },
        withImage
            ? new Dictionary<Guid, RenderImage> { [Asset] = new(Asset, "print", "image/jpeg", 900, 600, storageKey) }
            : new Dictionary<Guid, RenderImage>(),
        new Dictionary<Guid, RenderEntry> { [Entry] = new(Entry, "Ember Ward", fields, "A ring of embers.") },
        new Dictionary<Guid, RenderCitation> { [Use] = new(Use, "[{\"family\":\"Beck\",\"given\":\"Kent\"}]", "Extreme Programming Explained", 1999, "Addison-Wesley", "p. 41") });

    private static AssembledDocument Assemble(string body, TemplatePackage? template = null, bool withImage = true) =>
        ManuscriptAssembler.Assemble(Snapshot(body, withImage), template ?? Template(), "us-letter", new Dictionary<string, JsonElement>(), RenderTarget.Pdf);

    private static JsonArray Blocks(AssembledDocument document) => document.Ast["blocks"]!.AsArray();

    private static string Types(AssembledDocument document) => string.Join(",", Blocks(document).Select(b => b!["t"]!.GetValue<string>()));

    [Test]
    public void Assemble_WhenTheScopeIsAChapter_ShouldStartWithItsHeadingAndShiftProseHeadings()
    {
        var document = Assemble("# Inside\n\nText.\n");

        Types(document).ShouldBe("Header,Header,Para");
        var chapter = Blocks(document)[0]!["c"]!.AsArray();
        chapter[0]!.GetValue<int>().ShouldBe(2);
        chapter[1]![0]!.GetValue<string>().ShouldBe(ManuscriptAssembler.ChapterAnchor(Chapter1));
        Blocks(document)[1]!["c"]![0]!.GetValue<int>().ShouldBe(3);
    }

    [Test]
    public void Assemble_WhenTheSceneHasContainersAndLeafDirectives_ShouldGiveTemplateNeutralDivs()
    {
        var document = Assemble($":::sidebar{{title=\"Tip\"}}\nRead this.\n:::\n\n::pagebreak\n\n::beat{{id=\"{Guid.NewGuid()}\"}}\n\n::spell{{kb=\"{Entry}\"}}\n");

        var divs = Blocks(document).Skip(1).Select(b => b!["c"]![0]!).ToList();
        Types(document).ShouldBe("Header,Div,Div,Div");
        divs[0][1]![0]!.GetValue<string>().ShouldBe("sidebar");
        divs[0][2]![0]![1]!.GetValue<string>().ShouldBe("Tip");
        divs[1][1]![0]!.GetValue<string>().ShouldBe("pagebreak");
        divs[2][1]![0]!.GetValue<string>().ShouldBe("spell");
        divs[2][2]!.ToJsonString().ShouldContain("[\"data-field-casting-time\",\"1 action\"]");
    }

    [Test]
    public void Assemble_WhenTheFigureHasARendition_ShouldPlaceTheLocalFile()
    {
        var document = Assemble($"::figure{{id=\"{Figure}\"}}\n");

        var placed = document.Images.ShouldHaveSingleItem();
        placed.Source.ShouldBe("images/" + Asset.ToString("N") + ".jpg");
        Blocks(document)[1]!["t"]!.GetValue<string>().ShouldBe("Figure");
        Blocks(document)[1]!["c"]![0]![1]![0]!.GetValue<string>().ShouldBe("page");
        document.Flags.ShouldBeEmpty();
    }

    [Test]
    public void Assemble_WhenTheFigureHasNoRendition_ShouldDrawAPlaceholderAndFlagIt()
    {
        var document = Assemble($"::figure{{id=\"{Figure}\"}}\n", withImage: false);

        document.Images.ShouldBeEmpty();
        Blocks(document)[1]!["c"]![0]![1]![0]!.GetValue<string>().ShouldBe("missing-figure");
        document.Flags.ShouldHaveSingleItem().Kind.ShouldBe(RenderFlag.MissingAsset);
    }

    [Test]
    public void Assemble_WhenTheTemplateRefusesRawLatex_ShouldDropItAndFlagItAsNotKept()
    {
        var body = "```{=latex}\n\\newpage\n```\n";

        var kept = Assemble(body);
        var dropped = Assemble(body, Template(rawLatex: false));

        Types(kept).ShouldBe("Header,RawBlock");
        kept.RawLatex.ShouldHaveSingleItem().ShouldBe(new RenderFlag(RenderFlag.RawLatex, Scene, "\\newpage", true));
        Types(dropped).ShouldBe("Header");
        dropped.RawLatex.ShouldHaveSingleItem().Kept.ShouldBeFalse();
    }

    [Test]
    public void Assemble_WhenTheBodyHasAPipeTable_ShouldGiveATableWithAlignmentsHeaderAndInlineCells()
    {
        var document = Assemble($"| A | B | C |\n|:---|:---:|---:|\n| **bold** | [Departure](chapter:{Chapter2}) | `x` ~~old~~ |\n| only |\n");

        Types(document).ShouldBe("Header,Table");
        var table = Blocks(document)[1]!["c"]!.AsArray();
        table[2]!.AsArray().Select(c => c![0]!["t"]!.GetValue<string>()).ShouldBe(["AlignLeft", "AlignCenter", "AlignRight"]);
        table[3]![1]!.AsArray().ShouldHaveSingleItem()![1]!.AsArray().Count.ShouldBe(3);
        var body = table[4]![0]![3]!.AsArray();
        body.Count.ShouldBe(2);
        body[1]![1]!.AsArray().Count.ShouldBe(3);
        body[0]![1]![0]![4]![0]!["t"]!.GetValue<string>().ShouldBe("Plain");
        body[0]!.ToJsonString().ShouldContain("\"Strong\"");
        body[0]!.ToJsonString().ShouldContain("\"Strikeout\"");
        body[0]!.ToJsonString().ShouldContain("\"Code\"");
        document.Flags.ShouldBeEmpty();
    }

    [Test]
    public void Assemble_WhenTheBodyHasARuleAndAComment_ShouldGiveAHorizontalRuleAndDropTheCommentUnflagged()
    {
        var document = Assemble("One.\n\n---\n\n<!-- note\nmore -->\n\nTwo.\n");

        Types(document).ShouldBe("Header,Para,HorizontalRule,Para");
        document.Flags.ShouldBeEmpty();
    }

    [Test]
    public void Assemble_WhenTheBodyHasExternalLinksAndStrike_ShouldGiveLinkAndStrikeout()
    {
        var document = Assemble("Read [the rules](https://example.com/a?b=1) or [mail](mailto:k@example.com), ~~not~~ this; [x](ftp://nope).\n");

        var json = Blocks(document)[1]!.ToJsonString();
        json.ShouldContain("[\"https://example.com/a?b=1\",\"\"]");
        json.ShouldContain("[\"mailto:k@example.com\",\"\"]");
        json.ShouldContain("\"Strikeout\"");
        json.Split("\"Link\"").Length.ShouldBe(3);
    }

    [TestCase("<!-- note --> The gate opens.\n", "Text after an HTML comment is not printed")]
    [TestCase("Before.\n\n<!-- never closed\n\nThe gate opens.\n", "An unclosed HTML comment hides the rest of the scene")]
    [TestCase("> <!-- note --> quoted\n", "Text after an HTML comment is not printed")]
    public void Assemble_WhenACommentHidesAuthorText_ShouldFlagIt(string body, string detail)
    {
        var document = Assemble(body);

        document.Flags.ShouldHaveSingleItem().ShouldBe(new RenderFlag(RenderFlag.HiddenByComment, Scene, detail, false));
    }

    [Test]
    public void Assemble_WhenACommentHidesNothing_ShouldNotFlagIt()
    {
        Assemble("<!-- one line -->\n\n<!-- two\nlines -->   \n\nText.\n").Flags.ShouldBeEmpty();
    }

    [Test]
    public void Assemble_WhenTheBodyHasInlineRawLatex_ShouldKeepItOnlyWhereTheTemplateAllowsIt()
    {
        var body = "`\\DndDropCapLine{F}`{=latex}ighters and `code`.\n";

        var kept = Assemble(body);
        var dropped = Assemble(body, Template(rawLatex: false));
        var preview = ManuscriptAssembler.Assemble(Snapshot(body), Template(), "us-letter", new Dictionary<string, JsonElement>(), RenderTarget.Html);

        Blocks(kept)[1]!.ToJsonString().ShouldContain("[\"latex\",\"\\\\DndDropCapLine{F}\"]");

        Blocks(kept)[1]!.ToJsonString().ShouldContain("\"RawInline\"");
        kept.RawLatex.ShouldHaveSingleItem().ShouldBe(new RenderFlag(RenderFlag.RawLatex, Scene, "\\DndDropCapLine{F}", true));
        Blocks(dropped)[1]!.ToJsonString().ShouldNotContain("RawInline");
        dropped.RawLatex.ShouldHaveSingleItem().Kept.ShouldBeFalse();
        Blocks(preview)[1]!.ToJsonString().ShouldContain("raw-latex");
        Blocks(preview)[1]!.ToJsonString().ShouldNotContain("RawInline");
    }

    [Test]
    public void Assemble_WhenCodeIsFollowedByAChipAndAChipHoldsBackticks_ShouldPrintNeitherFenceBreakNorPadding()
    {
        var para = Blocks(Assemble("`x`<!---->`\\y`{=latex} then ``` ``Hello'' ```{=latex}\n"))[1]!;

        para.ToJsonString().ShouldNotContain("<!--");
        para["c"]!.AsArray().Where(n => n!["t"]!.GetValue<string>() == "RawInline").Select(n => n!["c"]![1]!.GetValue<string>())
            .ShouldBe(["\\y", "``Hello''"]);
    }

    [TestCase("- a\n\n  | x |\n  | --- |\n  | y |\n", "\"Table\"")]
    [TestCase("- a\n\n  ---\n", "\"HorizontalRule\"")]
    [TestCase("- a\n  1. b\n\n  - c\n", "\"OrderedList\"")]
    public void Assemble_WhenAListItemHoldsABlockAfterABlankLine_ShouldPrintItInsideTheItem(string body, string node)
    {
        var blocks = Blocks(Assemble(body));

        blocks.Count.ShouldBe(2);
        blocks[1]!["t"]!.GetValue<string>().ShouldBe("BulletList");
        blocks[1]!.ToJsonString().ShouldContain(node);
        blocks[1]!.ToJsonString().ShouldNotContain("|");
    }

    [Test]
    public void Assemble_WhenQuickPreviewMeetsARawLatexBlock_ShouldDrawALabelledPlaceholder()
    {
        var preview = ManuscriptAssembler.Assemble(Snapshot("```{=latex}\n\\newpage\n```\n"), Template(), "us-letter", new Dictionary<string, JsonElement>(), RenderTarget.Html);

        Types(preview).ShouldBe("Header,Div");
        Blocks(preview)[1]!.ToJsonString().ShouldContain("raw-latex");
        Blocks(preview)[1]!.ToJsonString().ShouldContain("raw-latex");
    }

    [Test]
    public void Assemble_WhenTheBodyHasNestedLists_ShouldNestThemInOneList()
    {
        var document = Assemble("- Supplies\n  - Rope\n  1. Oil\n- Weapons\n");

        Types(document).ShouldBe("Header,BulletList");
        Blocks(document)[1]!["c"]![0]!.AsArray().Select(b => b!["t"]!.GetValue<string>()).ShouldBe(["Plain", "BulletList", "OrderedList"]);
    }

    [Test]
    public void Assemble_WhenALineIsAnUnknownDirective_ShouldDropAndFlagIt()
    {
        var document = Assemble("::dropcap\n\nText.\n");

        Types(document).ShouldBe("Header,Para");
        document.Flags.ShouldHaveSingleItem().ShouldBe(new RenderFlag(RenderFlag.UnknownDirective, Scene, "::dropcap", false));
    }

    [Test]
    public void Assemble_WhenAChapterLinkNamesTheChapter_ShouldGiveItsNumberAndLinkOnlyWhenItIsRendered()
    {
        var document = Assemble($"See [Departure](chapter:{Chapter2}) and [the start](chapter:{Chapter1}).\n");

        var para = Blocks(document)[1]!.ToJsonString();
        para.ShouldContain("\"Chapter\"");
        para.ShouldContain("\"2:\"");
        para.ShouldNotContain("#ch-" + Chapter2);
        para.ShouldContain("\"#ch-" + Chapter1 + "\"");
    }

    [Test]
    public void Assemble_WhenAPassageIsCitedAndTheTemplateUsesFootnotes_ShouldFollowItWithANote()
    {
        var document = Assemble($"He [kept it small](ref:{Use}). [^](ref:{Use})\n");

        var para = Blocks(document)[1]!["c"]!.AsArray();
        para.Count(i => i!["t"]!.GetValue<string>() == "Note").ShouldBe(2);
        para.ToJsonString().ShouldContain("Addison-Wesley,");
    }

    [Test]
    public void Assemble_WhenTheTemplateUsesAuthorDate_ShouldAppendTheParenthetical()
    {
        var document = Assemble($"He [kept it small](ref:{Use}).\n", Template(citations: CitationForm.AuthorDate));

        var para = Blocks(document)[1]!.ToJsonString();
        para.ShouldNotContain("Note");
        para.ShouldContain("\"(Beck\"");
        para.ShouldContain("\"41)\"");
    }

    [Test]
    public void Assemble_WhenTheUseIsUnknown_ShouldFlagAMissingReference()
    {
        var document = Assemble($"A [^](ref:{Guid.NewGuid()}) marker.\n");

        document.Flags.ShouldHaveSingleItem().Kind.ShouldBe(RenderFlag.MissingReference);
    }

    [Test]
    public void Assemble_WhenTheBodyHasListsAndQuotes_ShouldNestTheirBlocks()
    {
        var document = Assemble("> Quoted *line*.\n\n3. First\n4. Second\n");

        Types(document).ShouldBe("Header,BlockQuote,OrderedList");
        Blocks(document)[2]!["c"]![0]![0]!.GetValue<int>().ShouldBe(3);
        Blocks(document)[2]!["c"]![1]![0]![0]!["t"]!.GetValue<string>().ShouldBe("Plain");
    }

    [Test]
    public void Assemble_WhenAnEntryFieldNameIsNotASafeName_ShouldLeaveItOutOfTheAttributes()
    {
        var fields = "{\"onclick\": \"alert(1)\", \"x\\\"><script>\": \"y\", \"range\": \"<b>30 ft</b>\"}";
        var document = ManuscriptAssembler.Assemble(
            Snapshot($"::spell{{kb=\"{Entry}\"}}\n", fields: fields), Template(), "us-letter", new Dictionary<string, JsonElement>(), RenderTarget.Html);

        var attributes = Blocks(document)[1]!["c"]![0]![2]!.AsArray().Select(a => a![0]!.GetValue<string>()).ToList();
        attributes.ShouldBe(["title", "data-field-onclick", "data-field-range"]);
    }

    [Test]
    public void Assemble_WhenAFenceNamesAnUnsafeLanguage_ShouldGiveTheCodeBlockNoClass()
    {
        var document = Assemble("```x\" onclick=\"y\nz\n```\n");

        Blocks(document)[1]!["c"]![0]![1]!.AsArray().ShouldBeEmpty();
    }

    [Test]
    public void Assemble_WhenTheBodyTheOptionsTheImageOrTheTemplateFilesChange_ShouldChangeTheContentHash()
    {
        const string body = "Text.\n\n::figure{id=\"00000000-0000-7000-8000-000000000006\"}\n";
        var options = new Dictionary<string, JsonElement> { ["drop-caps"] = JsonSerializer.SerializeToElement(true) };
        string Hash(string text, IReadOnlyDictionary<string, JsonElement> values, string key = "assets/map/print", string files = "a", string tools = "pandoc 3.12") =>
            ManuscriptAssembler.Assemble(Snapshot(text, storageKey: key), Template() with { ContentHash = files }, "us-letter", values, RenderTarget.Pdf, tools).ContentHash;

        var original = Hash(body, options);

        Hash(body, options).ShouldBe(original);
        Hash(body.Replace("Text.", "Other text."), options).ShouldNotBe(original);
        Hash(body, new Dictionary<string, JsonElement> { ["drop-caps"] = JsonSerializer.SerializeToElement(false) }).ShouldNotBe(original);
        Hash(body, options, key: "assets/map/print-2").ShouldNotBe(original);
        Hash(body, options, files: "b").ShouldNotBe(original);
        Hash(body, options, tools: "pandoc 3.13").ShouldNotBe(original);
    }
}
