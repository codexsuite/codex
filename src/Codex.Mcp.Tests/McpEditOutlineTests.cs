using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// J4 over stdio: move Shipping Small (Ch 7) before Discovery is Everyone's Job (Ch 6) and archive 8.1 in one
// edit_outline batch: a dry run first, then for real, then undo. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpEditOutlineTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private string _undoId = "";

    private McpClient Client => _server.Client;

    private static readonly object[] J4Ops =
    [
        new { op = "move", item = SeedIds.Chapters.Ch7.ToString(), before = SeedIds.Chapters.Ch6.ToString() },
        new { op = "archive", item = "8.1" },
    ];

    private static object J4 => new { book = ProductEngineering, ops = J4Ops };

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task EditOutline_WhenDryRun_ShouldReportRenumberingAndWriteNothing()
    {
        var body = await Client.OkAsync("edit_outline", new { book = ProductEngineering, ops = J4Ops, dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        Renumbered(body).ShouldBe([(SeedIds.Chapters.Ch6, "Ch 6", "Ch 7"), (SeedIds.Chapters.Ch7, "Ch 7", "Ch 6")], ignoreOrder: true);
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await ChapterNumberAsync(SeedIds.Chapters.Ch7)).ShouldBe(7);
        (await SceneArchivedAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBeFalse();
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenApplied_ShouldMoveTheChapterAndArchiveTheScene()
    {
        var body = await Client.OkAsync("edit_outline", J4);

        body.GetProperty("dry_run").GetBoolean().ShouldBeFalse();
        Renumbered(body).ShouldBe([(SeedIds.Chapters.Ch6, "Ch 6", "Ch 7"), (SeedIds.Chapters.Ch7, "Ch 7", "Ch 6")], ignoreOrder: true);
        body.GetProperty("results").GetArrayLength().ShouldBe(2);
        _undoId = body.GetProperty("undo_id").GetString()!;
        (await ChapterNumberAsync(SeedIds.Chapters.Ch7)).ShouldBe(6);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch6)).ShouldBe(7);
        (await SceneArchivedAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBeTrue();
        (await ChangeSetSourceAsync()).ShouldBe("agent");
    }

    [Test]
    [Order(3)]
    public async Task EditOutline_WhenAnOpFails_ShouldWriteNothing()
    {
        // The move is fine; the second op names a scene that does not exist, so the batch fails before anything lands.
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = ProductEngineering,
            ops = new object[]
            {
                new { op = "move", item = SeedIds.Chapters.Ch6.ToString(), before = SeedIds.Chapters.Ch7.ToString() },
                new { op = "archive", item = "42.1" },
            },
        });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ChapterNumberAsync(SeedIds.Chapters.Ch7)).ShouldBe(6);
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenGivenTheBatchUndoId_ShouldPutTheOutlineBack()
    {
        var body = await Client.OkAsync("undo", new { undo_id = _undoId });

        body.GetProperty("side_effects").GetArrayLength().ShouldBe(1);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch7)).ShouldBe(7);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch6)).ShouldBe(6);
        (await SceneArchivedAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBeFalse();
        (await SceneNumberAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBe(1);
    }

    [Test]
    [Order(5)]
    public async Task Undo_WhenAlreadyUndone_ShouldFailBlocked()
    {
        var error = await Client.ErrorAsync("undo", new { undo_id = _undoId });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("already_undone");
    }

    private static List<(Guid, string, string)> Renumbered(System.Text.Json.JsonElement body) =>
        body.GetProperty("renumbered").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetGuid(), r.GetProperty("from").GetString()!, r.GetProperty("to").GetString()!)).ToList();

    private async Task<int?> ChapterNumberAsync(Guid chapterId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int?>("SELECT chapter_number::int FROM chapter_outline WHERE id = @chapterId", new { chapterId });
    }

    private async Task<int> SceneNumberAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT n FROM (SELECT id, row_number() OVER (ORDER BY position, id)::int AS n FROM scene
                           WHERE chapter_id = (SELECT chapter_id FROM scene WHERE id = @sceneId) AND archived_at IS NULL) s
            WHERE id = @sceneId
            """, new { sceneId });
    }

    private async Task<bool> SceneArchivedAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM scene WHERE id = @sceneId", new { sceneId });
    }

    private async Task<string?> ChangeSetSourceAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string?>("SELECT source FROM change_set WHERE tool = 'edit_outline' ORDER BY created_at DESC LIMIT 1");
    }
}
