using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// get_outline returns a chapter's summary and plan notes under the same include flags as a scene's, and edit_outline's
// expected_summary / expected_plan_notes refuse to overwrite text the agent has not seen. One scenario, in order.
[TestFixture]
[CancelAfter(90000)]
public class McpOutlineChapterTextTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Ch3 = SeedIds.Chapters.Ch3.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        await Client.OkAsync("edit_outline", new
        {
            book = Book, ops = new object[] { new { op = "update", item = Ch3, summary = "Shape of three.", plan_notes = "Covers X." } },
        });
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<JsonElement> Chapter3Async(params string[] include)
    {
        var body = await Client.OkAsync("get_outline", new { book = Book, within = Ch3, include });
        return body.GetProperty("acts")[0].GetProperty("chapters")[0];
    }

    [Test]
    [Order(1)]
    public async Task GetOutline_WhenIncludingSummaries_ShouldReturnTheChapterSummaryButNotItsNotes()
    {
        var chapter = await Chapter3Async("summaries");

        chapter.GetProperty("summary").GetString().ShouldBe("Shape of three.");
        chapter.TryGetProperty("plan_notes", out var notes).ShouldBe(false, notes.ToString());
    }

    [Test]
    [Order(2)]
    public async Task GetOutline_WhenIncludingPlanNotes_ShouldReturnTheChapterNotesButNotItsSummary()
    {
        var chapter = await Chapter3Async("plan_notes");

        chapter.GetProperty("plan_notes").GetString().ShouldBe("Covers X.");
        chapter.TryGetProperty("summary", out var summary).ShouldBe(false, summary.ToString());
    }

    [Test]
    [Order(3)]
    public async Task GetOutline_WhenTheDefaultIncludesAreUsed_ShouldNotReturnChapterText()
    {
        var chapter = await Chapter3Async();

        chapter.TryGetProperty("plan_notes", out _).ShouldBe(false);
    }

    [Test]
    [Order(4)]
    public async Task EditOutline_WhenExpectedTextDiffersFromTheChapter_ShouldBeAConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book,
            ops = new object[] { new { op = "update", item = Ch3, title = "Should not stick", summary = "Clobber", expected_summary = "Stale text" } },
        });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<(string, string)>("SELECT title, summary FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch3 }))
            .Item2.ShouldBe("Shape of three.");
    }

    [Test]
    [Order(5)]
    public async Task EditOutline_WhenAConflictIsInALaterOp_ShouldRollBackTheEarlierOps()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book,
            ops = new object[]
            {
                new { op = "update", item = Ch3, plan_notes = "Rewritten" },
                new { op = "update", item = "4.1", summary = "x", expected_summary = "never matched" },
            },
        });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await Chapter3Async("plan_notes")).GetProperty("plan_notes").GetString().ShouldBe("Covers X.");
    }

    [Test]
    [Order(6)]
    public async Task EditOutline_WhenExpectedTextMatches_ShouldWriteTheChapterAndScene()
    {
        await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[]
            {
                new { op = "update", item = Ch3, plan_notes = "Covers X and Y.", expected_summary = "Shape of three.", expected_plan_notes = "Covers X." },
                new { op = "update", item = "4.1", summary = "Fresh scene summary", expected_plan_notes = "" },
            },
        });

        (await Chapter3Async("plan_notes")).GetProperty("plan_notes").GetString().ShouldBe("Covers X and Y.");
    }
}
