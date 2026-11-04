using System.Text.RegularExpressions;
using Codex.TestSupport;
using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.WebApi.Tests;

[TestFixture]
public partial class ProseCorpusTests
{
    private static readonly string CorpusDirectory = Path.Combine(RepoPaths.Root, "db", "prose-corpus");

    [GeneratedRegex(@"^\| `(?<file>[^`]+\.md)` \| (?<words>\d+) \|", RegexOptions.Multiline)]
    private static partial Regex WordsRow();

    [Test]
    public async Task Corpus_WhenWordCountedBySchema_ShouldMatchRecordedCounts()
    {
        var recorded = WordsRow()
            .Matches(await File.ReadAllTextAsync(Path.Combine(CorpusDirectory, "README.md")))
            .ToDictionary(m => m.Groups["file"].Value, m => int.Parse(m.Groups["words"].Value));
        var files = Directory.GetFiles(CorpusDirectory, "*.md").Select(Path.GetFileName).Where(n => n != "README.md").Order().ToList();
        recorded.Keys.Order().ShouldBe(files!);
        files.Count.ShouldBe(15);

        await using var database = await CodexDatabase.CloneSchemaAsync();
        await using var connection = await database.OpenAsync();
        foreach (var file in files)
        {
            var body = await File.ReadAllTextAsync(Path.Combine(CorpusDirectory, file!));
            await using var command = new NpgsqlCommand("SELECT markdown_word_count(@body)", connection);
            command.Parameters.AddWithValue("body", body);

            var words = (int)(await command.ExecuteScalarAsync())!;

            words.ShouldBe(recorded[file!], file);
        }
    }

    [Test]
    public async Task Corpus_ShouldUseCanonicalLayout()
    {
        foreach (var path in Directory.GetFiles(CorpusDirectory, "*.md").Where(p => Path.GetFileName(p) != "README.md"))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var text = System.Text.Encoding.UTF8.GetString(bytes);

            if (bytes.Length == 0)
            {
                continue;
            }
            text.EndsWith('\n').ShouldBeTrue(path);
            text.EndsWith("\n\n", StringComparison.Ordinal).ShouldBeFalse(path);
            text.ShouldNotContain("\r", customMessage: path);
            text.ShouldNotContain("\n\n\n", customMessage: path);
        }
    }
}
