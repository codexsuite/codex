using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Codex.Application.Features.Import;
using Codex.Application.Prose;
using Codex.TestSupport;

namespace Codex.UnitTest.Import;

// Generated round-trip cases for the importer's prose normaliser. The C# side generates inputs from a fixed seed (a mix
// of the tokens that trip Markdown readers), runs ImportProse.ToStored and pins the pairs in
// Fixtures/import/prose-generated.json; src/Codex.Client/src/features/library/importProse.test.ts checks every stored
// value with the TS reader (canonical, no directive nodes). The normaliser is C#, so the property runs over this
// exported set instead of fast-check inputs. After a deliberate change, regenerate with CODEX_WRITE_IMPORT_FIXTURES=1.
[TestFixture]
public class ImportProseGeneratedTests
{
    private const int Seed = 1010;
    private const int Count = 3000;

    private static readonly string[] Tokens =
    [
        "word", "Mara", "the", "ok.", "x", "é", " ", " ", " ", "\n", "\n", "\n\n", "*", "**", "***", "_", "__", "`", "``",
        "\\", "[", "]", "(", ")", "#", "## ", "# ", ">", "> ", "- ", "* ", "+ ", "1. ", "2) ", "  ", "::", ":::aside",
        "::beat{id=\"0199c0de-0000-7000-8000-000700000001\"}", "::pagebreak", "::spell{kb=\"0199c0de-0000-7000-8000-000700000002\"}",
        "[q](ref:0199c0de-0000-7000-8000-000700000003)", "[M](kb:0199c0de-0000-7000-8000-000700000004)", "* * *", "---",
        "```", "\t", "<b>", "&", "1.", "-", "\r\n", "\0",
    ];

    private static readonly string FixturePath =
        Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "prose-generated.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IReadOnlyList<GeneratedCase> Generate()
    {
        var random = new Random(Seed);
        var cases = new List<GeneratedCase>(Count);
        for (var n = 0; n < Count; n++)
        {
            var input = new StringBuilder();
            var length = random.Next(1, 24);
            for (var t = 0; t < length; t++)
            {
                input.Append(Tokens[random.Next(Tokens.Length)]);
            }
            cases.Add(new GeneratedCase(input.ToString(), ImportProse.ToStored(input.ToString())));
        }
        return cases;
    }

    [Test]
    public void Generated_ShouldMatchThePinnedFixture()
    {
        var cases = Generate();
        if (Environment.GetEnvironmentVariable("CODEX_WRITE_IMPORT_FIXTURES") == "1")
        {
            File.WriteAllText(FixturePath, JsonSerializer.Serialize(cases, Json) + "\n");
        }

        var pinned = JsonSerializer.Deserialize<List<GeneratedCase>>(File.ReadAllText(FixturePath))!;

        pinned.ShouldBe(cases);
    }

    [Test]
    public void Generated_ShouldHoldNoEmbedsAndNoNuls()
    {
        foreach (var (input, stored) in Generate())
        {
            ProseDocument.Parse(stored).ListEmbeds().ShouldBeEmpty($"input: {JsonSerializer.Serialize(input)}");
            stored.ShouldNotContain('\0');
        }
    }
}
