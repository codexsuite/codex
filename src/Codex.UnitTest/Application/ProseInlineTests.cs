using Codex.Application.Features.Print.Render;

namespace Codex.UnitTest.Application;

// The C# port of src/prose/markdown/inlineParse.ts that the print assembler reads paragraphs with.
[TestFixture]
public class ProseInlineTests
{
    private const string Entry = "0199c0de-0000-7000-8000-000b0000000b";
    private const string Use = "0199c0de-0000-7000-8000-000e00000001";
    private const string Chapter = "0199c0de-0000-7000-8000-00050000000b";

    private static string Show(string source) => string.Join(" | ", ProseInline.Parse(source).Select(leaf =>
        leaf.Kind switch
        {
            InlineLeafKind.Break => "<br>",
            InlineLeafKind.Mention => $"@{leaf.Text}",
            InlineLeafKind.RefMarker => "^",
            _ => leaf.Text,
        } + string.Concat(leaf.Marks.Select(m => "/" + m.Kind))));

    [TestCase("plain text", "plain text")]
    [TestCase("*a* **b** ***c***", "a/Italic |   | b/Bold |   | c/Italic/Bold")]
    [TestCase("a * b", "a  | * |  b")]
    [TestCase("_x_ and __y__", "x/Italic |  and  | y/Bold")]
    [TestCase("`*not*` \\*lit\\*", "*not*/Code |  *lit*")]
    [TestCase("one\ntwo", "one | <br> | two")]
    [TestCase("*a**b**c*", "a/Italic | b/Italic/Bold | c/Italic")]
    public void Parse_ShouldFollowTheInlineGrammar(string source, string expected)
    {
        Show(source).ShouldBe(expected);
    }

    [Test]
    public void Parse_ShouldReadTheThreeLinkSchemes()
    {
        var leaves = ProseInline.Parse($"[Kent Beck](kb:{Entry}) said [*small* steps](ref:{Use})[^](ref:{Use}), see [the start](chapter:{Chapter}).");

        Show($"[Kent Beck](kb:{Entry}) said [*small* steps](ref:{Use})[^](ref:{Use}), see [the start](chapter:{Chapter}).")
            .ShouldBe("@Kent Beck |  said  | small/Italic/Ref |  steps/Ref | ^ | , see  | the start/ChapterLink | .");
        leaves[0].Id.ShouldBe(Guid.Parse(Entry));
        leaves[2].Marks[^1].Id.ShouldBe(Guid.Parse(Use));
        leaves[4].Id.ShouldBe(Guid.Parse(Use));
        leaves[6].Marks[0].Id.ShouldBe(Guid.Parse(Chapter));
    }

    [TestCase("[x](kb:goodhart)")]
    [TestCase("[a [b] c](kb:0199c0de-0000-7000-8000-000b0000000b)")]
    [TestCase("[x](ftp://example.com)")]
    public void Parse_WhenTheLinkIsNotAnEmbed_ShouldKeepItAsText(string source)
    {
        ProseInline.Parse(source).ShouldAllBe(leaf => leaf.Kind == InlineLeafKind.Text && leaf.Marks.Count == 0);
    }

    [Test]
    public void Parse_WhenTheLinkIsExternal_ShouldMarkTheTextWithItsTarget()
    {
        var leaves = ProseInline.Parse("[x *y*](https://example.com/a) and [m](mailto:a@b.c)");

        leaves[0].Marks.ShouldBe([new InlineMark(InlineMarkKind.Link, null, "https://example.com/a")]);
        leaves[1].Marks.Select(m => m.Kind).ShouldBe([InlineMarkKind.Link, InlineMarkKind.Italic]);
        leaves[^1].Marks.Single().Href.ShouldBe("mailto:a@b.c");
    }

    [Test]
    public void Parse_WhenTextIsStruckThrough_ShouldMarkItStrike()
    {
        var leaves = ProseInline.Parse("a ~~b~~ c ~d~ ~~ e");

        leaves.Single(l => l.Marks.Any(m => m.Kind == InlineMarkKind.Strike)).Text.ShouldBe("b");
        string.Concat(leaves.Select(l => l.Text)).ShouldBe("a b c ~d~ ~~ e");
    }

    [Test]
    public void Parse_WhenAChipHoldsBackticks_ShouldStripTheOnePaddingSpace()
    {
        var leaves = ProseInline.Parse("``` ``Hello'' ```{=latex} and `` `x` `` and ` ` and `  a  `");

        leaves[0].Kind.ShouldBe(InlineLeafKind.RawLatex);
        leaves[0].Text.ShouldBe("``Hello''");
        Show("`` `x` `` and ` ` and `  a  `").ShouldBe("`x`/Code |  and  |  /Code |  and  |  a /Code");
    }

    [Test]
    public void Parse_WhenAFenceBreakSeparatesCodeFromAChip_ShouldDropIt()
    {
        var leaves = ProseInline.Parse("`x`<!---->`\\y`{=latex} and <!----> here");

        leaves.Select(l => l.Kind).Take(2).ShouldBe([InlineLeafKind.Text, InlineLeafKind.RawLatex]);
        string.Concat(leaves.Select(l => l.Text)).ShouldBe("x\\y and <!----> here");
    }

    [TestCase("a ~~~b~~ c")]
    [TestCase("[](https://x.test) end")]
    public void Parse_WhenTheRunOrLabelIsNotMarkup_ShouldKeepTheSourceAsText(string source)
    {
        ProseInline.Parse(source).ShouldAllBe(l => l.Kind == InlineLeafKind.Text && l.Marks.Count == 0);
        string.Concat(ProseInline.Parse(source).Select(l => l.Text)).ShouldBe(source);
    }

    [Test]
    public void Parse_WhenACodeSpanIsFollowedByLatexTag_ShouldBeRawLatex()
    {
        var leaves = ProseInline.Parse("`\\x{F}`{=latex}ighters `y`{=other}");

        leaves[0].Kind.ShouldBe(InlineLeafKind.RawLatex);
        leaves[0].Text.ShouldBe("\\x{F}");
        leaves[1].Text.ShouldBe("ighters ");
        leaves[2].Marks.Single().Kind.ShouldBe(InlineMarkKind.Code);
    }
}
