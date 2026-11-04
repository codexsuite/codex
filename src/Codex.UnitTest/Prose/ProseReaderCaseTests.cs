using System.Text.Json;
using Codex.Application.Prose;
using Codex.TestSupport;

namespace Codex.UnitTest.Prose;

// The TypeScript reader (src/prose/markdown/parse.ts) must read the same cases the same way:
// src/Codex.Client/src/prose/prose.readers.test.ts. Rules: docs/prose-format.md, "Reader rules".
[TestFixture]
public class ProseReaderCaseTests
{
    private static readonly JsonElement Fixture = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "prose", "reader-cases.json"))).RootElement;

    private static IEnumerable<TestCaseData> Cases() =>
        Fixture.GetProperty("cases").EnumerateArray().Select(c => new TestCaseData(
            c.GetProperty("markdown").GetString()!,
            Strings(c.GetProperty("blocks")),
            Strings(c.GetProperty("embeds"))).SetArgDisplayNames(c.GetProperty("name").GetString()!));

    private static IEnumerable<TestCaseData> Corpus() =>
        Fixture.GetProperty("corpus").EnumerateArray().Select(c => new TestCaseData(
            c.GetProperty("file").GetString()!,
            Strings(c.GetProperty("embeds"))).SetArgDisplayNames(c.GetProperty("file").GetString()!));

    [TestCaseSource(nameof(Cases))]
    public void ReaderCase_ShouldMatchSharedExpectation(string markdown, string[] blocks, string[] embeds)
    {
        var document = ProseDocument.Parse(markdown);

        document.Blocks.Select(b => BlockKind(b.Kind)).ShouldBe(blocks);
        Embeds(document).ShouldBe(embeds);
    }

    [TestCaseSource(nameof(Corpus))]
    public void Corpus_Embeds_ShouldMatchSharedExpectation(string file, string[] embeds)
    {
        var document = ProseDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "db", "prose-corpus", file)));

        Embeds(document).ShouldBe(embeds);
    }

    [Test]
    public void Corpus_EveryFile_ShouldHaveASharedExpectation()
    {
        var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "db", "prose-corpus"), "*.md")
            .Select(Path.GetFileName).Where(f => f != "README.md").Order(StringComparer.Ordinal);

        Fixture.GetProperty("corpus").EnumerateArray().Select(c => c.GetProperty("file").GetString()).ShouldBe(files);
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string[] Embeds(ProseDocument document) =>
        document.ListEmbeds().Select(e => $"{EmbedKind(e.Kind)}:{e.Id:D}").ToArray();

    private static string BlockKind(ProseBlockKind kind) => kind switch
    {
        ProseBlockKind.ContainerMarker => "container",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string EmbedKind(ProseEmbedKind kind) => kind switch
    {
        ProseEmbedKind.KbAddition => "kb-addition",
        ProseEmbedKind.StatBlock => "statblock",
        ProseEmbedKind.ChapterLink => "chapter",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
