using System.Text.Json;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// The access rule over MCP (#80): the real stdio process acting through Ana's token, which holds codex:read and
// codex:write. The seed shares Product Engineering with her to view and the Forsaken Gods series to write. A tool needs
// both its scope and the book role: her write token edits the series' books but is forbidden in Product Engineering,
// and what only the owner may do stays forbidden even where she can write.
[TestFixture]
[CancelAfter(120000)]
public class McpSharedBooksTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string PlayersGuide = SeedIds.Books.PlayersGuide.ToString();

    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartWithTokenAsync(async database =>
        (await TestTokens.CreateAsync(database.ConnectionString, SeedIds.Users.Ana, "Ana's agent", [ApiTokenScopes.Read, ApiTokenScopes.Write])).Secret);

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ListBooks_WhenBooksAreSharedWithTheUser_ShouldListThemWithTheirRoleAndOwner()
    {
        var body = await _server.Client.OkAsync("list_books");

        var productEngineering = body.GetProperty("standalone").EnumerateArray()
            .Single(b => b.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.Books.ProductEngineering);
        productEngineering.GetProperty("role").GetString().ShouldBe("view");
        productEngineering.GetProperty("owner").GetProperty("id").GetGuid().ShouldBe(SeedIds.Users.Jd);
        productEngineering.GetProperty("owner").GetProperty("display_name").GetString().ShouldBe("Joel Dickson");

        var series = body.GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.Series.ForsakenGods);
        series.GetProperty("role").GetString().ShouldBe("write");
        var books = series.GetProperty("books").EnumerateArray().ToList();
        books.Select(b => b.GetProperty("item").GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Books.PlayersGuide, SeedIds.Books.GamemastersCodex, SeedIds.Books.BestiaryOfTheFallen], ignoreOrder: true);
        books.ShouldAllBe(b => b.GetProperty("role").GetString() == "write" && b.GetProperty("owner").GetProperty("id").GetGuid() == SeedIds.Users.Jd);
        body.ToString().ShouldNotContain(SeedIds.Books.HollowThrone.ToString());
    }

    [Test]
    public async Task EditProse_WhenTheBookIsSharedToView_ShouldFailForbiddenAndLeaveTheProse()
    {
        var before = await ProseAsync(SeedIds.Scenes.Ch5Sc2);

        var error = await _server.Client.ErrorAsync("edit_prose", new
        {
            scene = SeedIds.Scenes.Ch5Sc2.ToString(), expected_version = before.Version,
            edits = new[] { new { old = before.Body[..Math.Min(20, before.Body.Length)], @new = "Ana was here" } },
        });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        (await ProseAsync(SeedIds.Scenes.Ch5Sc2)).ShouldBe(before);
    }

    [Test]
    public async Task EditOutline_WhenTheBookIsSharedToView_ShouldFailForbiddenAndAddNothing()
    {
        var scenes = await ScalarAsync<int>("SELECT count(*)::int FROM scene WHERE book_id = @book", new { book = SeedIds.Books.ProductEngineering });

        var error = await _server.Client.ErrorAsync("edit_outline", new
        {
            book = ProductEngineering, ops = new[] { new { op = "add", kind = "scene", title = "Ana's scene", before = "4.1" } },
        });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("message").GetString().ShouldBe("This book is shared with you to view only.");
        (await ScalarAsync<int>("SELECT count(*)::int FROM scene WHERE book_id = @book", new { book = SeedIds.Books.ProductEngineering }))
            .ShouldBe(scenes);
    }

    [Test]
    public async Task UpdateBook_WhenTheBookIsSharedToView_ShouldFailForbiddenAndKeepTheTitle()
    {
        var error = await _server.Client.ErrorAsync("update_book", new { book = ProductEngineering, fields = new { title = "Ana's now" } });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        (await ScalarAsync<string>("SELECT title FROM book WHERE id = @id", new { id = SeedIds.Books.ProductEngineering })).ShouldBe("Product Engineering");
    }

    [Test]
    public async Task EditOutline_WhenTheSeriesIsSharedToWrite_ShouldAddTheScene()
    {
        var body = await _server.Client.OkAsync("edit_outline", new
        {
            book = "Forsaken Gods/1", ops = new[] { new { op = "add", kind = "scene", title = "Ana's encounter", into = "1" } },
        });

        body.GetProperty("undo_id").GetString().ShouldStartWith("u_");
        (await ScalarAsync<int>("SELECT count(*)::int FROM scene WHERE book_id = @book AND title = 'Ana''s encounter'", new { book = SeedIds.Books.PlayersGuide }))
            .ShouldBe(1);
    }

    [Test]
    public async Task UpdateBook_WhenTheSeriesIsSharedToWriteAndTheCallReordersTheSeries_ShouldFailForbiddenOwnerOnly()
    {
        var error = await _server.Client.ErrorAsync("update_book", new { book = PlayersGuide, series_number = 3 });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("message").GetString().ShouldBe("Only the book's owner can do that.");
        (await ScalarAsync<int>("SELECT series_number FROM book WHERE id = @id", new { id = SeedIds.Books.PlayersGuide })).ShouldBe(1);
    }

    [Test]
    public async Task Undo_WhenTheWriteIsInABookSharedToView_ShouldFailForbiddenAndUndoNothing()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        var changeSet = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO change_set (book_id, tool, summary, undo, created_by)
            VALUES (@book, 'edit_outline', 'Added a scene', @undo::jsonb, @owner) RETURNING id
            """,
            new
            {
                book = SeedIds.Books.ProductEngineering, owner = SeedIds.Users.Jd,
                undo = JsonSerializer.Serialize(new[] { new { op = "archive_scene", id = SeedIds.Scenes.Ch5Sc2 } }),
            });

        var error = await _server.Client.ErrorAsync("undo", new { undo_id = $"u_{changeSet:N}" });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        (await ScalarAsync<bool>("SELECT undone_at IS NULL FROM change_set WHERE id = @id", new { id = changeSet })).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch5Sc2 })).ShouldBeTrue();
    }

    private async Task<(string Body, int Version)> ProseAsync(Guid scene)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>("SELECT body, version FROM prose WHERE scene_id = @scene", new { scene });
    }

    private async Task<T?> ScalarAsync<T>(string sql, object args)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args);
    }
}
