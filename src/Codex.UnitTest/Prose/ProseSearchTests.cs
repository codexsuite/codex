using Codex.Application.Prose;

namespace Codex.UnitTest.Prose;

[TestFixture]
public class ProseSearchTests
{
    private const string Id = "0199c0de-0000-7000-8000-000b0000000b";
    private const string Use = "0199c0de-0000-7000-8000-000e000000aa";

    private static SearchOptions Opts(string query, bool matchCase = false, bool wholeWord = false) => new(query, matchCase, wholeWord);

    [Test]
    public void Find_WhenTextIsPlain_ShouldReturnEveryMatchIgnoringCaseByDefault()
    {
        var matches = ProseSearch.Find("Teams ship. The teams argue.\n\nNo team here.", SearchKind.Prose, Opts("teams"));

        matches.Select(m => (m.Ordinal, m.Text, m.Status)).ShouldBe([(0, "Teams", MatchStatus.Replaceable), (1, "teams", MatchStatus.Replaceable)]);
    }

    [Test]
    public void Find_WhenMatchCaseOrWholeWord_ShouldNarrowTheMatches()
    {
        const string text = "Teams and teams and teammates.";

        ProseSearch.Find(text, SearchKind.Prose, Opts("teams", matchCase: true)).Count.ShouldBe(1);
        ProseSearch.Find(text, SearchKind.Prose, Opts("team", wholeWord: true)).Count.ShouldBe(0);
        ProseSearch.Find(text, SearchKind.Prose, Opts("teams", wholeWord: true)).Count.ShouldBe(2);
    }

    [Test]
    public void Find_WhenInDirectivesFencesOrLinkTargets_ShouldIgnoreThem()
    {
        var text = $"::beat{{id=\"{Id}\"}}\n\n```{{=latex}}\n\\teams\n```\n\n[see](kb:{Id}) plain.\n";

        ProseSearch.Find(text, SearchKind.Prose, Opts("beat")).ShouldBeEmpty();
        ProseSearch.Find(text, SearchKind.Prose, Opts("teams")).ShouldBeEmpty();
        ProseSearch.Find(text, SearchKind.Prose, Opts("kb")).ShouldBeEmpty();
        ProseSearch.Find(text, SearchKind.Prose, Opts("see")).Single().Status.ShouldBe(MatchStatus.Replaceable);
    }

    [Test]
    public void Replace_WhenMatchIsAMentionLabel_ShouldChangeTheLabelAndKeepTheTarget()
    {
        var text = $"Ask [Kent Beck](kb:{Id}) about it.\n";

        var outcome = ProseSearch.Replace(text, SearchKind.Prose, Opts("Kent Beck"), "Kenneth Beck");

        outcome.Text.ShouldBe($"Ask [Kenneth Beck](kb:{Id}) about it.\n");
        outcome.Replaced.ShouldBe(1);
    }

    [Test]
    public void Replace_WhenMatchIsInsideACitedPassage_ShouldSkipItAndLeaveTheTextUntouched()
    {
        var text = $"Design the teams [around the teams](ref:{Use}) well.\n";

        var outcome = ProseSearch.Replace(text, SearchKind.Prose, Opts("teams"), "groups");

        outcome.Text.ShouldBe($"Design the groups [around the teams](ref:{Use}) well.\n");
        (outcome.Replaced, outcome.Cited).ShouldBe((1, 1));
        ProseSearch.Replace($"[the teams](ref:{Use})\n", SearchKind.Prose, Opts("teams"), "groups").Text.ShouldBe($"[the teams](ref:{Use})\n");
    }

    [Test]
    public void Replace_WhenMatchSpansALinkBoundary_ShouldReportItAndSkipIt()
    {
        var text = $"Kent [Beck](kb:{Id}) wrote it.\n";

        var matches = ProseSearch.Find(text, SearchKind.Prose, Opts("Kent Beck"));
        var outcome = ProseSearch.Replace(text, SearchKind.Prose, Opts("Kent Beck"), "KB");

        matches.Single().Status.ShouldBe(MatchStatus.SpansLink);
        (outcome.Text, outcome.Replaced, outcome.Spans).ShouldBe((text, 0, 1));
    }

    [Test]
    public void Replace_WhenOnlyAnOrdinalIsGiven_ShouldChangeJustThatMatch()
    {
        var outcome = ProseSearch.Replace("a teams b teams c teams", SearchKind.Prose, Opts("teams"), "groups", onlyOrdinal: 1);

        outcome.Text.ShouldBe("a teams b groups c teams");
        outcome.Replaced.ShouldBe(1);
    }

    [Test]
    public void Replace_WhenTheReplacementHoldsMarkdownCharacters_ShouldEscapeThem()
    {
        var outcome = ProseSearch.Replace("one two\n", SearchKind.Prose, Opts("two"), "a*b_[c]");

        outcome.Text.ShouldBe("one a\\*b\\_\\[c\\]\n");
        ProseSearch.Find(outcome.Text, SearchKind.Prose, Opts("a*b_[c]")).Count.ShouldBe(1);
    }

    [Test]
    public void Find_WhenTheSourceHasEscapes_ShouldMatchTheVisibleText()
    {
        var matches = ProseSearch.Find("Use a \\*star\\* here.\n", SearchKind.Prose, Opts("*star*"));

        matches.Count.ShouldBe(1);
        ProseSearch.Replace("Use a \\*star\\* here.\n", SearchKind.Prose, Opts("*star*"), "moon").Text.ShouldBe("Use a moon here.\n");
    }

    [Test]
    public void Replace_WhenPlainText_ShouldKeepTheReplacementLiteral()
    {
        ProseSearch.Replace("Write the *hook*.", SearchKind.Plain, Opts("hook"), "a*b").Text.ShouldBe("Write the *a*b*.");
    }

    [Test]
    public void Find_WhenNothingMatchesOrQueryEmpty_ShouldReturnNoMatches()
    {
        ProseSearch.Find("abc", SearchKind.Prose, Opts("zzz")).ShouldBeEmpty();
        ProseSearch.Find("abc", SearchKind.Prose, Opts("")).ShouldBeEmpty();
    }
}
