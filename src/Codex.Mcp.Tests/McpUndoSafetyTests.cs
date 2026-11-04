using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// undo's safety (M14): an id expires after 24 hours, a step undo can't run is blocked, an edit the author made since turns
// undo into a conflict that writes nothing (and gives the id back), and a second undo of the same id is refused.
[TestFixture]
[CancelAfter(60000)]
public class McpUndoSafetyTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task Undo_WhenTheAuthorEditedSinceInTheUi_ShouldConflictWriteNothingAndKeepTheIdUsable()
    {
        var write = await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[] { new { op = "update", item = "4.1", summary = "Agent summary." }, new { op = "archive", item = "8.1" } },
        });
        var undoId = write.GetProperty("undo_id").GetString();
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync("UPDATE scene SET summary = 'Author summary.' WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc1 });

        var error = await Client.ErrorAsync("undo", new { undo_id = undoId });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await connection.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch8Sc1 })).ShouldBeTrue();
        (await connection.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc1 })).ShouldBe("Author summary.");

        await connection.ExecuteAsync("UPDATE scene SET summary = 'Agent summary.' WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc1 });
        await Client.OkAsync("undo", new { undo_id = undoId });
        (await connection.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch8Sc1 })).ShouldBeFalse();

        var again = await Client.ErrorAsync("undo", new { undo_id = undoId });
        again.GetProperty("code").GetString().ShouldBe("blocked");
        again.GetProperty("rule").GetString().ShouldBe("already_undone");
    }

    [Test]
    [Order(2)]
    public async Task Undo_WhenTheIdIsOlderThanADay_ShouldBeBlockedAsExpired()
    {
        var write = await Client.OkAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "update", item = "4.1", summary = "Old news." } } });
        var undoId = write.GetProperty("undo_id").GetString()!;
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync("UPDATE change_set SET created_at = now() - interval '25 hours' WHERE id = @id", new { id = Id(undoId) });

        var error = await Client.ErrorAsync("undo", new { undo_id = undoId });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("undo_expired");
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenAStepIsUnsupported_ShouldBeBlockedAndWriteNothing()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        var id = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO change_set (book_id, tool, summary, undo)
            VALUES (@book, 'edit_outline', 'Added a chapter', @undo::jsonb) RETURNING id
            """,
            new { book = SeedIds.Books.ProductEngineering, undo = $"[{{\"op\":\"unsupported\",\"id\":\"{Guid.CreateVersion7()}\",\"title\":\"a chapter\"}}]" });

        var error = await Client.ErrorAsync("undo", new { undo_id = "u_" + id.ToString("N") });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("undo_unsupported");
        (await connection.ExecuteScalarAsync<bool>("SELECT undone_at IS NULL FROM change_set WHERE id = @id", new { id })).ShouldBeTrue();
    }

    private static Guid Id(string undoId) => Guid.Parse(undoId["u_".Length..]);
}
