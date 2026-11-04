using Codex.Application.Prose;
using Codex.TestSupport;

namespace Codex.UnitTest.Prose;

[TestFixture]
public class ProseDocumentTests
{
    private static readonly string CorpusDirectory = Path.Combine(RepoPaths.Root, "db", "prose-corpus");

    private static readonly Guid Beat3 = Guid.Parse("0199c0de-0000-7000-8000-000700000003");
    private static readonly Guid Addition = Guid.Parse("0199c0de-0000-7000-8000-000800000001");
    private static readonly Guid Goodhart = Guid.Parse("0199c0de-0000-7000-8000-000b00000005");
    private static readonly Guid BeckEntry = Guid.Parse("0199c0de-0000-7000-8000-000b0000000b");
    private static readonly Guid Use = Guid.Parse("0199c0de-0000-7000-8000-000e000000aa");

    private static string Corpus(string file) => File.ReadAllText(Path.Combine(CorpusDirectory, file));

    private static IEnumerable<string> CorpusFiles() =>
        Directory.GetFiles(CorpusDirectory, "*.md").Where(f => Path.GetFileName(f) != "README.md").Select(Path.GetFileName)!;

    [TestCaseSource(nameof(CorpusFiles))]
    public void Corpus_WhenParsedAndWritten_ShouldRoundTrip(string file)
    {
        var body = Corpus(file);

        ProseDocument.Parse(body).ToString().ShouldBe(body);
    }

    [Test]
    public void Blocks_OnCorpus_ShouldCoverOnlyWholeLines()
    {
        foreach (var file in CorpusFiles())
        {
            var body = Corpus(file);
            foreach (var block in ProseDocument.Parse(body).Blocks)
            {
                (block.Start == 0 || body[block.Start - 1] == '\n').ShouldBeTrue(file);
                (block.End == body.Length || body[block.End] == '\n').ShouldBeTrue(file);
            }
        }
    }

    [Test]
    public void InsertAfterBeat_ShouldPlaceAfterLine()
    {
        var body = Corpus("02-beats.md");

        var result = ProseDocument.Parse(body).InsertAfterBeat(Beat3, "Inserted paragraph.\n").ToString();

        const string beatLine = "::beat{id=\"0199c0de-0000-7000-8000-000700000003\"}\n";
        result.ShouldBe(body.Replace(beatLine + "\n", beatLine + "\nInserted paragraph.\n\n", StringComparison.Ordinal));
    }

    [Test]
    public void InsertAfterBeat_WhenBeatIsLast_ShouldAppendWithCanonicalEnding()
    {
        var result = ProseDocument.Parse("::beat{id=\"0199c0de-0000-7000-8000-000700000003\"}\n").InsertAfterBeat(Beat3, "Text");

        result.ToString().ShouldBe("::beat{id=\"0199c0de-0000-7000-8000-000700000003\"}\n\nText\n");
    }

    [Test]
    public void InsertAfterBeat_WhenBeatUnknown_ShouldThrowTargetNotFound()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse(Corpus("02-beats.md")).InsertAfterBeat(Guid.NewGuid(), "x"));

        thrown.Failure.ShouldBe(ProseEditFailure.TargetNotFound);
    }

    [Test]
    public void InsertAfterBeat_WhenBeatOnlyInCodeSpan_ShouldThrowTargetNotFound()
    {
        var body = "A paragraph with `::beat{id=\"0199c0de-0000-7000-8000-000700000099\"}` in code.\n";

        Should.Throw<ProseEditException>(() => ProseDocument.Parse(body).InsertAfterBeat(Guid.Parse("0199c0de-0000-7000-8000-000700000099"), "x"));
    }

    [Test]
    public void InsertAfterBeat_WhenEmptyMarkdown_ShouldThrowEmptyInsert()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse(Corpus("02-beats.md")).InsertAfterBeat(Beat3, "\n"));

        thrown.Failure.ShouldBe(ProseEditFailure.EmptyInsert);
    }

    private const string B1 = "0199c0de-0000-7000-8000-000700000001";
    private const string B3 = "0199c0de-0000-7000-8000-000700000003";

    [Test]
    public void ReplaceBeatProse_WhenProseThenAnotherBeat_ShouldReplaceOnlyThatProse()
    {
        var body = Corpus("02-beats.md");

        var result = ProseDocument.Parse(body).ReplaceBeatProse(Guid.Parse(B1), "New one.\n\nNew two.\n").ToString();

        result.ShouldBe($"::beat{{id=\"{B1}\"}}\n\nNew one.\n\nNew two.\n\n" + body[body.IndexOf($"::beat{{id=\"{B3}\"", StringComparison.Ordinal)..]);
    }

    [Test]
    public void ReplaceBeatProse_WhenBeatHasNoProse_ShouldInsertBeforeTheNextBeat()
    {
        var body = Corpus("02-beats.md");

        var result = ProseDocument.Parse(body).ReplaceBeatProse(Beat3, "Text").ToString();

        result.ShouldContain($"::beat{{id=\"{B3}\"}}\n\nText\n\n::beat{{id=\"0199c0de-0000-7000-8000-000700000006\"}}");
        result.ShouldEndWith("grew under the beat above.\n");
    }

    [Test]
    public void ReplaceBeatProse_WhenBeatIsLast_ShouldReplaceToTheEnd()
    {
        var body = $"::beat{{id=\"{B1}\"}}\n\nOld one.\n\nOld two.\n";

        ProseDocument.Parse(body).ReplaceBeatProse(Guid.Parse(B1), "Text\n").ToString().ShouldBe($"::beat{{id=\"{B1}\"}}\n\nText\n");
    }

    [Test]
    public void ReplaceBeatProse_WhenProseHoldsEmbeds_ShouldDropThemWithIt()
    {
        var addition = "0199c0de-0000-7000-8000-000800000001";
        var body = $"::beat{{id=\"{B1}\"}}\n\nA [gh](kb:{Goodhart}) and [m](ref:{Use}).\n\n::kb-addition{{id=\"{addition}\"}}\n";

        var result = ProseDocument.Parse(body).ReplaceBeatProse(Guid.Parse(B1), "Fresh.");

        result.ListEmbeds().Select(e => e.Id).ShouldBe([Guid.Parse(B1)]);
        result.ToString().ShouldBe($"::beat{{id=\"{B1}\"}}\n\nFresh.\n");
    }

    [Test]
    public void ReplaceBeatProse_WhenBeatUnknownOrTextEmpty_ShouldThrow()
    {
        var doc = ProseDocument.Parse(Corpus("02-beats.md"));

        Should.Throw<ProseEditException>(() => doc.ReplaceBeatProse(Guid.NewGuid(), "x")).Failure.ShouldBe(ProseEditFailure.TargetNotFound);
        Should.Throw<ProseEditException>(() => doc.ReplaceBeatProse(Beat3, "\n")).Failure.ShouldBe(ProseEditFailure.EmptyInsert);
    }

    [Test]
    public void ReplaceBeatProse_WhenBeatOnlyInCodeSpan_ShouldThrowTargetNotFound()
    {
        var body = "A paragraph with `::beat{id=\"0199c0de-0000-7000-8000-000700000099\"}` in code.\n";

        Should.Throw<ProseEditException>(() => ProseDocument.Parse(body).ReplaceBeatProse(Guid.Parse("0199c0de-0000-7000-8000-000700000099"), "x"));
    }

    [Test]
    public void WrapRef_ThenUnwrap_ShouldRestoreOriginal()
    {
        var body = Corpus("01-paragraphs-and-marks.md");
        var first = body.Split('\n')[0];
        var passage = first.Split(' ').Skip(1).Take(3).Aggregate((a, b) => a + " " + b);

        var wrapped = ProseDocument.Parse(body).WrapRef(passage, Use);

        wrapped.ToString().ShouldContain($"[{passage}](ref:{Use})");
        wrapped.ToString().Length.ShouldBe(body.Length + 1 + 6 + 36 + 1);
        wrapped.UnwrapRef(Use).ToString().ShouldBe(body);
    }

    [Test]
    public void WrapMention_ShouldWrapTheFirstPlainWholeWordOccurrence()
    {
        var body = "Kentucky has `Kent` in code, [Kent](ref:0199c0de-0000-7000-8000-000e000000aa) cited, then Kent and Kent.\n";

        var wrapped = ProseDocument.Parse(body).WrapMention("Kent", BeckEntry);

        wrapped.ToString().ShouldBe(body.Replace("then Kent and", $"then [Kent](kb:{BeckEntry}) and", StringComparison.Ordinal));
        wrapped.ListEmbeds().Where(e => e.Kind == ProseEmbedKind.Mention).Select(e => e.Id).ShouldBe([BeckEntry]);
        wrapped.ReplaceMentionWithText(BeckEntry).ToString().ShouldBe(body);
    }

    [Test]
    public void WrapMention_WhenNoPlainOccurrence_ShouldThrowPassageNotWrappable()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse("Kentucky only, and `Kent`.\n").WrapMention("Kent", BeckEntry));

        thrown.Failure.ShouldBe(ProseEditFailure.PassageNotWrappable);
    }

    [Test]
    public void WrapRef_ByRange_ShouldWrapOnlyThatRange()
    {
        var body = "One two three.\n";

        var wrapped = ProseDocument.Parse(body).WrapRef(4, 3, Use);

        wrapped.ToString().ShouldBe($"One [two](ref:{Use}) three.\n");
    }

    [TestCase("```{=latex}\n\\textbf{x}\n```\n", "textbf")]
    [TestCase("Some `code span` here.\n", "code span")]
    [TestCase("A [Kent Beck](kb:0199c0de-0000-7000-8000-000b0000000b) mention.\n", "Kent Beck")]
    [TestCase("Plain text.\n", "missing")]
    [TestCase("Has [bracket here.\n", "[bracket")]
    public void WrapRef_WhenPassageUnwrappable_ShouldThrow(string body, string passage)
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse(body).WrapRef(passage, Use));

        thrown.Failure.ShouldBe(ProseEditFailure.PassageNotWrappable);
    }

    [TestCase("A `` ` [Kent](kb:0199c0de-0000-7000-8000-000b0000000b) `` span.\n")]
    [TestCase("A ``` ``[Kent](kb:0199c0de-0000-7000-8000-000b0000000b)`` ```{=latex} chip.\n")]
    public void ListEmbeds_WhenLinkIsInsideCodeSpanHoldingBackticks_ShouldIgnoreIt(string body)
    {
        ProseDocument.Parse(body).ListEmbeds().ShouldBeEmpty();
    }

    [Test]
    public void WrapRef_WhenFirstOccurrenceIsInCode_ShouldWrapLaterOccurrence()
    {
        var wrapped = ProseDocument.Parse("Use `alpha` and alpha.\n").WrapRef("alpha", Use);

        wrapped.ToString().ShouldBe($"Use `alpha` and [alpha](ref:{Use}).\n");
    }

    [Test]
    public void UnwrapRef_OnCorpusMark_ShouldLeaveItsTextAndRemoveBareMarker()
    {
        var body = Corpus("05-ref-marks.md");
        var doc = ProseDocument.Parse(body);

        var sharedUse = Guid.Parse("0199c0de-0000-7000-8000-000e00000007");
        var unwrapped = doc.UnwrapRef(sharedUse).ToString();
        unwrapped.ShouldNotContain(sharedUse.ToString());
        unwrapped.ShouldContain("A quotation can run across paragraphs. First paragraph of the quote, which repeats the use id.\n\nSecond paragraph");

        var bare = doc.UnwrapRef(Guid.Parse("0199c0de-0000-7000-8000-000e0000000b")).ToString();
        bare.ShouldContain("A bare marker follows a paraphrase.\n");
    }

    [Test]
    public void UnwrapRef_WhenUnknown_ShouldThrowTargetNotFound()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse(Corpus("05-ref-marks.md")).UnwrapRef(Guid.NewGuid()));

        thrown.Failure.ShouldBe(ProseEditFailure.TargetNotFound);
    }

    [Test]
    public void StripKbAddition_ShouldRemoveOnlyThatLine()
    {
        var body = Corpus("03-kb-addition.md");
        const string line = "::kb-addition{id=\"0199c0de-0000-7000-8000-000800000001\"}\n\n";

        ProseDocument.Parse(body).StripKbAddition(Addition).ToString().ShouldBe(body.Replace(line, string.Empty, StringComparison.Ordinal));
    }

    [Test]
    public void StripKbAddition_WhenLastBlock_ShouldKeepSingleTrailingNewline()
    {
        var body = Corpus("11-edge-cases.md");

        var result = ProseDocument.Parse(body).StripKbAddition(Addition).ToString();

        result.ShouldBe(body[..body.LastIndexOf("\n::kb-addition", StringComparison.Ordinal)]);
    }

    [Test]
    public void StripFigure_ShouldRemoveOnlyThatLineAndOneBlankLine()
    {
        var figure = Guid.Parse("0199c0de-0000-7000-8000-001200000001");
        var body = $"First.\n\n::figure{{id=\"{figure}\"}}\n\nSecond.\n";

        ProseDocument.Parse(body).StripFigure(figure).ToString().ShouldBe("First.\n\nSecond.\n");
    }

    [Test]
    public void StripFigure_WhenUnknown_ShouldThrowTargetNotFound()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse("Words.\n").StripFigure(Guid.NewGuid()));

        thrown.Failure.ShouldBe(ProseEditFailure.TargetNotFound);
    }

    [Test]
    public void StripKbAddition_WhenOnlyBlock_ShouldGiveEmptyBody()
    {
        ProseDocument.Parse("::kb-addition{id=\"0199c0de-0000-7000-8000-000800000001\"}\n").StripKbAddition(Addition).ToString().ShouldBe(string.Empty);
    }

    [Test]
    public void StripKbAddition_WhenUnknown_ShouldThrowTargetNotFound()
    {
        var thrown = Should.Throw<ProseEditException>(() => ProseDocument.Parse(Corpus("03-kb-addition.md")).StripKbAddition(Guid.NewGuid()));

        thrown.Failure.ShouldBe(ProseEditFailure.TargetNotFound);
    }

    [Test]
    public void ReplaceMentionWithText_ShouldKeepName()
    {
        var body = Corpus("03-kb-addition.md");

        var result = ProseDocument.Parse(body).ReplaceMentionWithText(Goodhart).ToString();

        result.ShouldBe(body.Replace("[Goodhart's Law](kb:0199c0de-0000-7000-8000-000b00000005)", "Goodhart's Law", StringComparison.Ordinal));
    }

    [Test]
    public void ReplaceMentionWithText_WhenRepeatedAndFormatted_ShouldReplaceAll()
    {
        var result = ProseDocument.Parse(Corpus("04-mentions-and-links.md")).ReplaceMentionWithText(BeckEntry).ToString();

        result.ShouldNotContain(BeckEntry.ToString());
        result.ShouldStartWith("Early in his career, Kent Beck wrote about feedback. Kent Beck's habits");
    }

    [Test]
    public void ReplaceMentionWithText_WhenNoMention_ShouldReturnSameDocument()
    {
        var doc = ProseDocument.Parse(Corpus("03-kb-addition.md"));

        doc.ReplaceMentionWithText(Guid.NewGuid()).ShouldBeSameAs(doc);
    }

    [Test]
    public void ListEmbeds_OnCorpus_ShouldFindAll()
    {
        Embeds("02-beats.md", ProseEmbedKind.Beat).ShouldBe(3);
        Embeds("03-kb-addition.md", ProseEmbedKind.KbAddition).ShouldBe(1);
        Embeds("03-kb-addition.md", ProseEmbedKind.Mention).ShouldBe(1);
        Embeds("04-mentions-and-links.md", ProseEmbedKind.Mention).ShouldBe(8);
        Embeds("04-mentions-and-links.md", ProseEmbedKind.ChapterLink).ShouldBe(3);
        Embeds("05-ref-marks.md", ProseEmbedKind.Ref).ShouldBe(7);
        Embeds("07-print-directives.md", ProseEmbedKind.Figure).ShouldBe(1);
        Embeds("07-print-directives.md", ProseEmbedKind.Spell).ShouldBe(2);
        Embeds("07-print-directives.md", ProseEmbedKind.StatBlock).ShouldBe(1);
        Embeds("08-containers.md", ProseEmbedKind.Mention).ShouldBe(1);
        Embeds("12-empty.md", ProseEmbedKind.Beat).ShouldBe(0);
    }

    [Test]
    public void ListEmbeds_OnEdgeCases_ShouldIgnoreDirectiveTextInCodeSpan()
    {
        var embeds = ProseDocument.Parse(Corpus("11-edge-cases.md")).ListEmbeds();

        embeds.Select(e => e.Kind).ShouldBe([ProseEmbedKind.Beat, ProseEmbedKind.KbAddition]);
    }

    [Test]
    public void ListEmbeds_ShouldReportOffsetsAndText()
    {
        var body = Corpus("05-ref-marks.md");

        var bare = ProseDocument.Parse(body).ListEmbeds().Single(e => e.Id == Guid.Parse("0199c0de-0000-7000-8000-000e0000000b"));

        bare.Text.ShouldBe("^");
        body.Substring(bare.Start, bare.Length).ShouldBe("[^](ref:0199c0de-0000-7000-8000-000e0000000b)");
        var escaped = ProseDocument.Parse(body).ListEmbeds().Single(e => e.Id == Guid.Parse("0199c0de-0000-7000-8000-000e0000000f"));
        escaped.Text.ShouldBe("kept \\[as\\] written");
    }

    private const string TableBody = "Before.\n\n| Name | Note |\n|:---|---:|\n| Kent Beck | small batches |\n| Other | x |\n\nAfter.\n";

    [Test]
    public void Blocks_WhenTableRuleAndCommentArePresent_ShouldReadThemAsTheirOwnBlocks()
    {
        var body = Corpus("14-more-markdown.md") + "\n" + Corpus("13-tables.md");

        var kinds = ProseDocument.Parse(body).Blocks.Select(b => b.Kind).ToList();

        kinds.ShouldContain(ProseBlockKind.Rule);
        kinds.Count(k => k == ProseBlockKind.Table).ShouldBe(3);
        kinds.Count(k => k == ProseBlockKind.Opaque).ShouldBe(1);
        kinds.Count(k => k == ProseBlockKind.List).ShouldBe(2);
    }

    [Test]
    public void WrapRef_WhenInsideATableCellThenUnwrapped_ShouldRestoreTheTable()
    {
        var wrapped = ProseDocument.Parse(TableBody).WrapRef("small batches", Use);

        wrapped.ToString().ShouldContain($"| Kent Beck | [small batches](ref:{Use}) |");
        wrapped.ListEmbeds().Single().Kind.ShouldBe(ProseEmbedKind.Ref);
        wrapped.UnwrapRef(Use).ToString().ShouldBe(TableBody);
    }

    [Test]
    public void WrapMention_WhenInsideATableCellThenReplaced_ShouldRestoreTheTable()
    {
        var wrapped = ProseDocument.Parse(TableBody).WrapMention("Kent Beck", BeckEntry);

        wrapped.ToString().ShouldContain($"| [Kent Beck](kb:{BeckEntry}) | small batches |");
        wrapped.ListEmbeds().Single().Text.ShouldBe("Kent Beck");
        wrapped.ReplaceMentionWithText(BeckEntry).ToString().ShouldBe(TableBody);
    }

    [TestCase("Name | Note")]
    [TestCase("---")]
    [TestCase("Beck | small")]
    public void WrapRef_WhenThePassageCrossesACellOrHitsTheDelimiterRow_ShouldThrow(string passage)
    {
        Should.Throw<ProseEditException>(() => ProseDocument.Parse(TableBody).WrapRef(passage, Use))
            .Failure.ShouldBe(ProseEditFailure.PassageNotWrappable);
    }

    [Test]
    public void InsertAfterBeat_NextToATable_ShouldKeepTheTableIntact()
    {
        var body = $"::beat{{id=\"{Beat3}\"}}\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n---\n";

        var result = ProseDocument.Parse(body).InsertAfterBeat(Beat3, "New.").ToString();

        result.ShouldBe($"::beat{{id=\"{Beat3}\"}}\n\nNew.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n---\n");
    }

    [Test]
    public void ListEmbeds_WhenAnHtmlCommentHoldsALink_ShouldNotReadIt()
    {
        var body = $"<!-- [x](kb:{Goodhart}) -->\n\nText [y](kb:{BeckEntry}).\n";

        ProseDocument.Parse(body).ListEmbeds().Select(e => e.Id).ShouldBe([BeckEntry]);
    }

    private static int Embeds(string file, ProseEmbedKind kind) =>
        ProseDocument.Parse(Corpus(file)).ListEmbeds().Count(e => e.Kind == kind);
}
