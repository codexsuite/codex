using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// edit_outline's all-or-nothing promise: an op that fails while the batch is being applied rolls back what the
// earlier ops (and the failing one's own first commands) wrote, including a chapter it added, and an anchor
// archived earlier in the batch is rejected. add with create_missing_labels works, and undo removes the label again.
[TestFixture]
[CancelAfter(60000)]
public class McpEditOutlineAtomicTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Ch4 = SeedIds.Chapters.Ch4.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task EditOutline_WhenALaterOpFailsWhileApplying_ShouldRollBackTheEarlierOpsAndTheLabelsItMade()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book,
            ops = new object[]
            {
                new { op = "update", item = "4.1", summary = "Changed by a batch that fails." },
                new { op = "add", kind = "scene", into = Ch4, title = "Doomed", labels = new[] { "Doomed label" }, create_missing_labels = true },
                new { op = "update", item = "5.2", status = "not-a-status" },
            },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc1 }))
            .ShouldNotBe("Changed by a batch that fails.");
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM label WHERE name = 'Doomed label' AND archived_at IS NULL")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM scene WHERE title = 'Doomed' AND archived_at IS NULL")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenAnOpFailsAfterTheBatchAddedAChapter_ShouldArchiveTheChapterAgain()
    {
        var ops = new object[]
        {
            new { op = "add", kind = "chapter", title = "Not yet", after = SeedIds.Chapters.Ch7.ToString() },
            new { op = "update", item = "5.2", status = "not-a-status" },
        };

        var error = await Client.ErrorAsync("edit_outline", new { book = Book, ops });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM chapter WHERE title = 'Not yet' AND archived_at IS NULL")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int?>("SELECT chapter_number::int FROM chapter_outline WHERE id = @id", new { id = SeedIds.Chapters.Ch8 }))
            .ShouldBe(8);
    }

    [Test]
    [Order(3)]
    public async Task EditOutline_WhenAnAnchorWasArchivedEarlierInTheBatch_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book, dry_run = true,
            ops = new object[] { new { op = "archive", item = "8.1" }, new { op = "move", item = "4.1", after = "8.1" } },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(4)]
    public async Task EditOutline_WhenAddingASceneWithMissingLabelsAndAMention_ShouldCreateThemAndUndoShouldRemoveTheLabel()
    {
        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[]
            {
                new
                {
                    op = "add", kind = "scene", into = Ch4, title = "Fresh", summary = "Shows @[Goodhart's Law].",
                    labels = new[] { "Fresh label" }, create_missing_labels = true,
                },
            },
        });

        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM label WHERE name = 'Fresh label'")).ShouldBe(1);
        (await connection.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE title = 'Fresh' AND archived_at IS NULL"))
            .ShouldBe($"Shows [Goodhart's Law](kb:{SeedIds.KbEntries.GoodhartsLaw}).");

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM label WHERE name = 'Fresh label' AND archived_at IS NULL")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene WHERE title = 'Fresh' AND archived_at IS NULL")).ShouldBe(0);
    }
}
