using Codex.Application.Features.References;

namespace Codex.UnitTest.References;

[TestFixture]
public class PermissionLetterTests
{
    private static PermissionLetterFacts Facts(Func<PermissionLetterFacts, PermissionLetterFacts>? change = null)
    {
        var facts = new PermissionLetterFacts(
            "Reinertsen Estate", "The Velocity Trap", "Reinertsen", 2009, "Celeritas", "Product Engineering", "Joel Dickson",
            342, 0, 0, ["pp. 41-43", "p. 118"], [4, 8], "Northgate Press", 2027, 3000, ["print", "pdf"], "World English");
        return change is null ? facts : change(facts);
    }

    [Test]
    public void Draft_ShouldNameTheWordsPagesChaptersAndThePrintRun()
    {
        var letter = PermissionLetter.Draft(Facts());

        letter.ShouldStartWith("Dear Reinertsen Estate,");
        letter.ShouldContain("*The Velocity Trap* (2009) by Reinertsen in *Product Engineering* by Joel Dickson");
        letter.ShouldContain("342 words of quoted prose (pp. 41-43 and p. 118; Chapters 4 and 8)");
        letter.ShouldContain("published by Northgate Press in 2027 in print and PDF with a first print run of 3,000 copies for World English distribution");
        letter.ShouldEndWith("Joel Dickson");
    }

    [Test]
    public void Draft_WhenSettingsAreMissing_ShouldLeaveTheirSentenceOut()
    {
        var letter = PermissionLetter.Draft(Facts(f => f with
        {
            RightsHolder = null, SourcePublisher = null, Publisher = null, PlannedYear = null, PrintRun = null, Formats = [], Territory = null,
        }));

        letter.ShouldStartWith("Dear Rights Holder,");
        letter.ShouldNotContain("The book will be");
    }

    [Test]
    public void Draft_WhenLyricsAndFigures_ShouldListEachKindOfMaterial()
    {
        var letter = PermissionLetter.Draft(Facts(f => f with { QuotedWords = 0, LyricWords = 40, Figures = 2, Locators = [], Chapters = [10] }));

        letter.ShouldContain("The material is 40 words of lyrics or poetry and 2 figures (Chapter 10).");
    }
}
