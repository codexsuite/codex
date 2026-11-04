using Codex.Application.Mcp;

namespace Codex.UnitTest.Mcp;

// The guard against ids an agent invents: every form in which the database triggers would still read a ::beat,
// ::kb-addition or ::figure line, or a ref: link, out of new text.
[TestFixture]
public class McpProseRulesTests
{
    private static readonly Guid Id = Guid.Parse("0199c0de-0000-7000-8000-000700000003");

    [TestCaseSource(nameof(Bypasses))]
    public void RefuseInvented_WhenTheTextCarriesANewId_ShouldBeInvalid(string text)
    {
        var e = Should.Throw<McpToolException>(() => McpProseRules.RefuseInvented("text", "Plain words.\n", text));

        e.Code.ShouldBe("invalid");
    }

    public static IEnumerable<TestCaseData> Bypasses()
    {
        yield return new TestCaseData($"Words.\n\n::beat{{id=\"{Id}\"}}\n").SetName("beat line");
        yield return new TestCaseData($"Words.\n\n::beat{{id=\"{Id}\"}} \n").SetName("beat line, trailing space");
        yield return new TestCaseData($"Words.\n\n::beat{{id=\"{Id}\"}}\t\n").SetName("beat line, trailing tab");
        yield return new TestCaseData($"```\n::beat{{id=\"{Id}\"}}\n```\n").SetName("beat in a fence");
        yield return new TestCaseData($":::foo\n::beat{{id=\"{Id}\"}}\n:::\n").SetName("beat in a container");
        yield return new TestCaseData($"> ::beat{{id=\"{Id}\"}}\n").SetName("beat in a quote");
        yield return new TestCaseData($"- ::beat{{id=\"{Id}\"}}\n").SetName("beat in a list");
        yield return new TestCaseData($"::kb-addition{{id=\"{Id}\"}}\n").SetName("kb-addition line");
        yield return new TestCaseData($"::figure{{id=\"{Id}\"}}\n").SetName("figure line");
        yield return new TestCaseData($"[x](ref:{Id})").SetName("ref link");
        yield return new TestCaseData($"```\n[x](ref:{Id})\n```\n").SetName("ref in a fence");
        yield return new TestCaseData($"`[x](ref:{Id})`").SetName("ref in a code span");
        yield return new TestCaseData($"[first\n\nsecond](ref:{Id})").SetName("ref across paragraphs");
        yield return new TestCaseData($"::unknown\n[x](ref:{Id})\n").SetName("ref on an unknown directive line");
        yield return new TestCaseData($":::foo\n[x](ref:{Id})\n:::\n").SetName("ref in a container");
    }

    [Test]
    public void RefuseInvented_WhenTheTextAddsAFigureLine_ShouldNamePlaceFigure()
    {
        var e = Should.Throw<McpToolException>(() => McpProseRules.RefuseInvented("text", "Plain words.\n", $"::figure{{id=\"{Id}\"}}\n"));

        e.Message.ShouldContain("place_figure");
    }

    [Test]
    public void RefuseInvented_WhenTheIdWasAlreadyInTheOldText_ShouldPass()
    {
        var body = $"Words.\n\n::beat{{id=\"{Id}\"}}\n\n[x](ref:{Id})\n";

        Should.NotThrow(() => McpProseRules.RefuseInvented("edits", body, body.Replace("Words.", "Other words.", StringComparison.Ordinal)));
    }

    [Test]
    public void RefuseInvented_WhenPlainMarkdownAndMentions_ShouldPass()
    {
        Should.NotThrow(() => McpProseRules.RefuseInvented("text", "", $"Some *words* and [a name](kb:{Id}).\n"));
    }

    [Test]
    public void RefsTouched_WhenARefPassageChanged_ShouldNameTheUse()
    {
        McpProseRules.RefsTouched($"a [old](ref:{Id}) b", $"a [new](ref:{Id}) b").ShouldBe([Id]);
        McpProseRules.RefsTouched($"a [same](ref:{Id}) b", $"a [same](ref:{Id}) c").ShouldBeEmpty();
    }
}
