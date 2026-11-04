using System.Text.RegularExpressions;
using Codex.Application.Features.Import;
using Codex.Application.Prose;
using Codex.TestSupport;

namespace Codex.UnitTest.Import;

// The Players Guide fixture (src/Codex.TestSupport/Fixtures/players-guide, README there): `mapped/` must be what the
// mapper makes of `source/`. Set CODEX_REGEN_PLAYERS_GUIDE=1 to rewrite `mapped/` instead of comparing.
[TestFixture]
public partial class PlayersGuideFixtureTests
{
    private static readonly string Root = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "players-guide");

    private static IEnumerable<string> Chapters() => Enumerable.Range(1, 10).Select(n => $"chapter-{n:00}.md");

    private static string Source(string chapter) =>
        File.ReadAllText(Path.Combine(Root, "source", chapter)).TrimStart('\uFEFF').Replace("\r\n", "\n");

    private static string Mapped(string chapter) => File.ReadAllText(Path.Combine(Root, "mapped", chapter));

    [TestCaseSource(nameof(Chapters))]
    public void Mapped_WhenChapter_ShouldEqualTheMapperOutputForSource(string chapter)
    {
        var expected = LatexImportMapper.Map(Source(chapter)).Prose;
        if (Environment.GetEnvironmentVariable("CODEX_REGEN_PLAYERS_GUIDE") == "1")
        {
            File.WriteAllText(Path.Combine(Root, "mapped", chapter), expected);
        }

        Mapped(chapter).ShouldBe(expected);
    }

    [Test]
    public void Mapped_WhenAllChapters_ShouldKeepEveryPipeTableLineUnchanged()
    {
        var lines = Chapters().SelectMany(c => Source(c).Split('\n')).Where(l => l.StartsWith('|')).ToList();

        lines.Count.ShouldBe(522);
        foreach (var chapter in Chapters())
        {
            var mapped = Mapped(chapter).Split('\n').ToList();
            var expected = Source(chapter).Split('\n').Where(l => l.StartsWith('|')).ToList();
            mapped.Where(l => l.StartsWith('|')).ShouldBe(expected);
        }
    }

    [TestCaseSource(nameof(Chapters))]
    public void Mapped_WhenChapter_ShouldHoldNoLatexCommandOutsideFencesSpansAndComments(string chapter)
    {
        var offenders = new List<string>();
        var state = "";
        foreach (var line in Mapped(chapter).Split('\n'))
        {
            if (state == "fence")
            {
                state = line == "```" ? "" : state;
            }
            else if (state == "comment")
            {
                state = line.Contains("-->", StringComparison.Ordinal) ? "" : state;
            }
            else if (line.StartsWith("```", StringComparison.Ordinal))
            {
                state = "fence";
            }
            else if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                state = line.Contains("-->", StringComparison.Ordinal) ? "" : "comment";
            }
            else if (Command().IsMatch(InlineSpan().Replace(line, "")))
            {
                offenders.Add(line);
            }
        }

        offenders.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(Chapters))]
    public void Mapped_WhenChapter_ShouldBeCanonicalProse(string chapter)
    {
        var text = Mapped(chapter);

        text.ShouldEndWith("\n");
        text.ShouldNotEndWith("\n\n");
        text.ShouldNotContain('\r');
    }

    // HTML comments are opaque by the format (docs/prose-format.md); nothing else may be.
    [TestCaseSource(nameof(Chapters))]
    public void ProseDocument_WhenMappedChapter_ShouldHoldNoOpaqueBlockExceptHtmlComments(string chapter)
    {
        var text = Mapped(chapter);

        var opaque = ProseDocument.Parse(text).Blocks
            .Where(b => b.Kind == ProseBlockKind.Opaque)
            .Select(b => text[b.Start..b.End])
            .Where(raw => !raw.StartsWith("<!--", StringComparison.Ordinal));

        opaque.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(Chapters))]
    public void ProseDocument_WhenMappedChapter_ShouldReadEveryPipeLineAsATable(string chapter)
    {
        var text = Mapped(chapter);
        var tables = ProseDocument.Parse(text).Blocks.Where(b => b.Kind == ProseBlockKind.Table).ToList();

        var outside = PipeLineStarts(text).Where(start => !tables.Any(t => t.Start <= start && start < t.End));

        outside.ShouldBeEmpty();
    }

    [Test]
    public void Mapped_WhenAllChapters_ShouldCountWhatItMappedAndFenced()
    {
        var results = Chapters().Select(c => LatexImportMapper.Map(Source(c))).ToList();

        TestContext.Out.WriteLine(string.Join("\n", results.Select((r, n) =>
            $"chapter-{n + 1:00}: {string.Join(", ", r.Mapped.OrderBy(m => m.Key).Select(m => $"{m.Key}={m.Value}"))}")));
        results.Sum(r => r.Mapped.GetValueOrDefault(LatexImportRule.DropCap)).ShouldBeGreaterThan(0);
    }

    private static IEnumerable<int> PipeLineStarts(string text)
    {
        var start = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('|'))
            {
                yield return start;
            }
            start += line.Length + 1;
        }
    }

    [GeneratedRegex(@"(?<!\\)\\[a-zA-Z]+")]
    private static partial Regex Command();

    [GeneratedRegex(@"(`+)[^`\n]+?\1\{=latex\}")]
    private static partial Regex InlineSpan();
}
