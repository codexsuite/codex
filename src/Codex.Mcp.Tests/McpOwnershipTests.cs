using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// The ownership rule over MCP (#29): the real stdio process acting as user B (CODEX_MCP_USER_EMAIL), whose library is
// empty, is handed JD's books, scenes, beats, drafts, entries and change sets. Every read and write answers not_found,
// exactly as for an id that does not exist, and nothing of JD's changes. A JD title does not resolve for B either.
[TestFixture]
[CancelAfter(60000)]
public class McpOwnershipTests
{
    private const string UserBEmail = "b@example.test";
    private static readonly string HollowThrone = SeedIds.Books.HollowThrone.ToString();
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();

    private McpStdioServer _server = null!;
    private Guid _jdChangeSet;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsUserAsync(async database =>
        {
            await TestUsers.CreateAsync(database.ConnectionString, UserBEmail, "User B");
            return UserBEmail;
        });
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        // A write JD's agent made: undoing it would archive JD's scene.
        _jdChangeSet = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO change_set (book_id, tool, summary, undo, created_by)
            VALUES (@book, 'edit_outline', 'Added a scene', @undo::jsonb, @owner) RETURNING id
            """,
            new
            {
                book = SeedIds.Books.ProductEngineering, owner = SeedIds.Users.Jd,
                undo = JsonSerializer.Serialize(new[] { new { op = "archive_scene", id = SeedIds.Scenes.Ch5Sc2 } }),
            });
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ListBooks_WhenTheUserHasNoBooks_ShouldListNoneOfJds()
    {
        var body = await _server.Client.OkAsync("list_books");

        body.ToString().ShouldNotContain(HollowThrone);
        body.ToString().ShouldNotContain("Hollow Throne");
    }

    [TestCase("read_manuscript")]
    [TestCase("get_outline")]
    [TestCase("get_book_settings")]
    [TestCase("list_kb_entries")]
    public async Task ReadTool_WhenGivenAnotherUsersBookId_ShouldFailNotFound(string tool)
    {
        var error = await _server.Client.ErrorAsync(tool, new { book = HollowThrone });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [TestCase("Hollow Throne")]
    [TestCase("The Ashen Crown/3")]
    public async Task GetOutline_WhenGivenAnotherUsersBookTitle_ShouldNotResolveIt(string book)
    {
        var error = await _server.Client.ErrorAsync("get_outline", new { book });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        error.ToString().ShouldNotContain(HollowThrone);
    }

    [Test]
    public async Task ReadManuscript_WhenGivenAnItemRefInAnotherUsersBook_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = "Product Engineering", scope = "5.2" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        error.ToString().ShouldNotContain(ProductEngineering);
    }

    [Test]
    public async Task GetKbEntry_WhenGivenAnotherUsersEntry_ShouldFailNotFound()
    {
        var byId = await _server.Client.ErrorAsync("get_kb_entry", new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString() });
        var byName = await _server.Client.ErrorAsync("get_kb_entry", new { entry = "Goodhart's Law" });

        byId.GetProperty("code").GetString().ShouldBe("not_found");
        byName.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task Search_WhenTheTextIsInAnotherUsersBooks_ShouldFindNothing()
    {
        var body = await _server.Client.OkAsync("search", new { query = "Goodhart" });

        body.ToString().ShouldNotContain(SeedIds.KbEntries.GoodhartsLaw.ToString());
    }

    [Test]
    public async Task GetDraftAndJob_WhenGivenAnotherUsersDraft_ShouldFailNotFound()
    {
        var draft = await _server.Client.ErrorAsync("get_draft", new { draft = SeedIds.AiGenerations.Ch5Sc2Pending });
        var job = await _server.Client.ErrorAsync("get_job", new { job_id = SeedIds.AiGenerations.Ch5Sc2Pending });

        draft.GetProperty("code").GetString().ShouldBe("not_found");
        job.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task EditProse_WhenGivenAnotherUsersScene_ShouldFailNotFoundAndLeaveTheProse()
    {
        var before = await ProseAsync(SeedIds.Scenes.Ch5Sc2);

        var error = await _server.Client.ErrorAsync("edit_prose", new
        {
            scene = SeedIds.Scenes.Ch5Sc2.ToString(), expected_version = before.Version,
            edits = new[] { new { old = before.Body[..Math.Min(20, before.Body.Length)], @new = "B was here" } },
        });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ProseAsync(SeedIds.Scenes.Ch5Sc2)).ShouldBe(before);
    }

    [Test]
    public async Task MergeSources_WhenGivenAnotherUsersSources_ShouldFailNotFoundAndLeaveThem()
    {
        var byId = await _server.Client.ErrorAsync("merge_sources",
            new { source = SeedIds.References.VelocityTrap.ToString(), into = SeedIds.References.Accelerate.ToString() });
        var byMarker = await _server.Client.ErrorAsync("merge_sources", new { source = "#4", into = "#8", book = ProductEngineering });

        byId.GetProperty("code").GetString().ShouldBe("not_found");
        byMarker.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeTrue();
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ArchiveSource_WhenGivenAnotherUsersSource_ShouldFailNotFoundAndLeaveIt()
    {
        var error = await _server.Client.ErrorAsync("archive_source", new { source = SeedIds.References.VelocityTrap.ToString() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeTrue();
    }

    [Test]
    public async Task AddSource_WhenGivenAnotherUsersBook_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("add_source",
            new { book = ProductEngineering, source = new { @new = new { kind = "book", title = "B's source" } } });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE title = @title", new { title = "B's source" })).ShouldBe(0);
    }

    [Test]
    public async Task ListRevisions_WhenGivenAnotherUsersScene_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("list_revisions", new { scene = SeedIds.Scenes.Ch4Sc1.ToString() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task UpsertBeat_WhenGivenAnotherUsersBeat_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("upsert_beat", new { beat = SeedIds.Beats.Ch3Sc1EarlyAgileYears, instruction = "B's beat" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<string>("SELECT body FROM beat WHERE id = @id", new { id = SeedIds.Beats.Ch3Sc1EarlyAgileYears }))
            .ShouldNotBe("B's beat");
    }

    [Test]
    public async Task UpdateBook_WhenGivenAnotherUsersBook_ShouldFailNotFoundAndKeepTheTitle()
    {
        var error = await _server.Client.ErrorAsync("update_book", new { book = HollowThrone, fields = new { title = "Mine now" } });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<string>("SELECT title FROM book WHERE id = @id", new { id = SeedIds.Books.HollowThrone })).ShouldBe("Hollow Throne");
    }

    [Test]
    public async Task ResolveDraft_WhenGivenAnotherUsersDraft_ShouldFailNotFoundAndLeaveItPending()
    {
        var error = await _server.Client.ErrorAsync("resolve_draft", new { generation = SeedIds.AiGenerations.Ch5Sc2Pending, action = "discard" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending }))
            .ShouldBe("pending");
    }

    [Test]
    public async Task Undo_WhenGivenAnotherUsersChangeSet_ShouldFailNotFoundAndUndoNothing()
    {
        var error = await _server.Client.ErrorAsync("undo", new { undo_id = $"u_{_jdChangeSet:N}" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ScalarAsync<bool>("SELECT undone_at IS NULL FROM change_set WHERE id = @id", new { id = _jdChangeSet })).ShouldBeTrue();
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
