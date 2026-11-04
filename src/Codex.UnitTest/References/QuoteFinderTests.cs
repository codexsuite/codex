using Codex.Application.Features.References;

namespace Codex.UnitTest.References;

[TestFixture]
public class QuoteFinderTests
{
    private const string Use = "0199c0de-0000-7000-8000-000e00000002";

    [Test]
    public void Find_WhenAQuoteHasNoMarker_ShouldReturnItsWordsWithoutTheMarks()
    {
        var found = QuoteFinder.Find("The facilitator said, \"The customer is not in the building\", and everyone laughed.\n");

        found.ShouldBe(["The customer is not in the building"]);
    }

    [Test]
    public void Find_WhenTheMarksAreCurly_ShouldFindThemToo()
    {
        var found = QuoteFinder.Find("She wrote “Speed is not the same as progress at all” in the margin.\n");

        found.ShouldBe(["Speed is not the same as progress at all"]);
    }

    [Test]
    public void Find_WhenTheQuoteHoldsARefLink_ShouldIgnoreIt()
    {
        var found = QuoteFinder.Find($"Opens with a value: \"[Individuals and interactions over processes and tools](ref:{Use})\". Then more.\n");

        found.ShouldBeEmpty();
    }

    [Test]
    public void Find_WhenTheQuoteSitsInsideARefLink_ShouldIgnoreIt()
    {
        var found = QuoteFinder.Find($"[He said \"this is not the end of it\" twice](ref:{Use}) and left.\n");

        found.ShouldBeEmpty();
    }

    [Test]
    public void Find_WhenABareMarkerFollowsTheQuote_ShouldIgnoreIt()
    {
        var found = QuoteFinder.Find($"As Brooks put it, \"adding manpower makes it later\".[^](ref:{Use})\n");

        found.ShouldBeEmpty();
    }

    [Test]
    public void Find_WhenTheQuoteIsShort_ShouldIgnoreIt()
    {
        var found = QuoteFinder.Find("The words \"Today\" and \"not yet\" are scare quotes.\n");

        found.ShouldBeEmpty();
    }

    [Test]
    public void Find_WhenTheQuoteHoldsLinksAndEmphasis_ShouldKeepTheLabelsOnly()
    {
        var found = QuoteFinder.Find("He said \"ask [Team Atlas](kb:0199c0de-0000-7000-8000-000b00000008) about the *real* cost\" once.\n");

        found.ShouldBe(["ask Team Atlas about the real cost"]);
    }

    [Test]
    public void Find_WhenInsideABlockQuote_ShouldDropTheCaret()
    {
        var found = QuoteFinder.Find("> He said \"we never ship on\n> a Friday afternoon\" loudly.\n");

        found.ShouldBe(["we never ship on a Friday afternoon"]);
    }

    [Test]
    public void Find_WhenTheSameQuoteRepeats_ShouldReturnItOnce()
    {
        var found = QuoteFinder.Find("\"one two three four\" and again \"one two three four\".\n");

        found.ShouldBe(["one two three four"]);
    }

    [Test]
    public void Find_WhenTheQuoteIsInADirectiveOrFence_ShouldIgnoreIt()
    {
        var found = QuoteFinder.Find("::beat{id=\"0199c0de-0000-7000-8000-000700000002\"}\n\n```{=latex}\n\"one two three four\"\n```\n");

        found.ShouldBeEmpty();
    }
}
