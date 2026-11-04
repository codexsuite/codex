using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Fakes;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// The #64 constructs (pipe tables, rules, strike, external links, inline raw LaTeX) pass through the prose tools as
// plain Markdown: submit_draft and edit_prose accept them, read_manuscript returns them unchanged. One scenario on
// Ch 4 · Sc 1 of its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpRichMarkdownProseTests
{
    private const string Rich =
        "| Road | Distance |\n|:---|---:|\n| **North** | 40 miles |\n| ~~Coast~~ | [map](https://example.com/m) |\n\n---\n\nA `\\textsc{word}`{=latex} here.";

    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private McpInProcessServer _server = null!;
    private string _bodyBefore = "";
    private int _version;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpInProcessServer.StartAsync(new FakeProseGenerator([], Task.CompletedTask));
        (_bodyBefore, _version) = await ProseAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task SubmitDraft_WhenTheTextHasTablesRulesStrikeLinksAndInlineLatex_ShouldStoreItUnchanged()
    {
        var result = await Client.OkAsync("submit_draft", new { scene = Scene, text = Rich });

        var draft = (await Client.OkAsync("get_draft", new { draft = result.GetProperty("generation").GetString() })).GetProperty("draft");
        draft.GetProperty("text").GetString().ShouldBe(Rich);
    }

    [Test]
    [Order(2)]
    public async Task EditProse_WhenTheEditAddsConstructsFromTheFormat_ShouldWriteThemAndReadManuscriptShouldReturnThemUnchanged()
    {
        var phrase = _bodyBefore.TrimEnd('\n').Split('\n')[0];

        var edited = await Client.OkAsync("edit_prose", new { scene = Scene, expected_version = _version, edits = new[] { new { old = phrase, @new = Rich } } });

        edited.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        var expected = _bodyBefore.Replace(phrase, Rich, StringComparison.Ordinal);
        (await ProseAsync()).Body.ShouldBe(expected);
        var read = (await Client.OkAsync("read_manuscript", new { book = Book, scope = "4.1", format = "markdown" }))
            .GetProperty("scenes").EnumerateArray().Single();
        read.GetProperty("body").GetString().ShouldBe(expected);
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch4Sc1 });
    }
}
