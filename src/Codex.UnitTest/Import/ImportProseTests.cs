using System.Text.Json;
using Codex.Application.Features.Import;
using Codex.Application.Prose;
using Codex.TestSupport;

namespace Codex.UnitTest.Import;

// The importer's prose normaliser. Fixtures/import/prose-cases.json is shared with
// src/Codex.Client/src/features/library/importProse.test.ts, which checks every `stored` value is canonical.
[TestFixture]
public class ImportProseTests
{
    private static IEnumerable<TestCaseData> Cases() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "prose-cases.json")))
            .RootElement.EnumerateArray()
            .Select(c => new TestCaseData(c.GetProperty("input").GetString()!, c.GetProperty("stored").GetString()!)
                .SetArgDisplayNames(c.GetProperty("name").GetString()!));

    [TestCaseSource(nameof(Cases))]
    public void ToStored_WhenCase_ShouldMatchSharedExpectation(string input, string stored)
    {
        ImportProse.ToStored(input).ShouldBe(stored);
    }

    [TestCaseSource(nameof(Cases))]
    public void ToStored_WhenCase_ShouldBeAFixedPointWithNoEmbeds(string input, string stored)
    {
        ImportProse.ToStored(stored).ShouldBe(stored);
        ProseDocument.Parse(ImportProse.ToStored(input)).ListEmbeds().ShouldBeEmpty();
    }

    [Test]
    public void ToStored_WhenOrderedNumberIsEscapedWithAParen_ShouldStoreItAsPlainText()
    {
        // "1\)" is how a writer says the paren is text; the format reads only "1." as a list, so the stored body drops the escape.
        ImportProse.ToStored("1\\) not a list").ShouldBe("1) not a list\n");
        ImportProse.ToStored("1) a list").ShouldBe("1. a list\n");
    }
}
