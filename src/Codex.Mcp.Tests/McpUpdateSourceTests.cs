using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// update_source over stdio on the Product Engineering seed. One scenario on its own clone, in order: fields set (and put
// back by undo), the cite key and permissions contact, and the refusals. The use-level changes (retype, remove) moved to
// update_source_use (M1/M10; McpUpdateSourceUseTests). Re-checking identifiers goes to the network (ISourceLookup), so it is covered by the References lane's API tests.
[TestFixture]
[CancelAfter(120000)]
public class McpUpdateSourceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private string _fieldsUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args) ?? throw new InvalidOperationException("no value");
    }

    [Test]
    [Order(1)]
    public async Task UpdateSource_WhenFieldsAreSet_ShouldChangeThemAndRecordTheHistoryAsTheAgent()
    {
        var body = await Client.OkAsync("update_source", new
        {
            source = "#10", book = Book, fields = new { publisher = "Reissue Press", accessed_on = "2026-10-19", rights_holder = "The Estate" },
        });

        (await ScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("Reissue Press");
        (await ScalarAsync<string>("SELECT rights_holder FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("The Estate");
        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.MythicalManMonth);
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'details_edited' ORDER BY created_at DESC LIMIT 1",
            new { id = SeedIds.References.MythicalManMonth })).ShouldBe("agent");
        (await ScalarAsync<string>("SELECT client_name FROM reference_event WHERE reference_id = @id AND source = 'agent' LIMIT 1",
            new { id = SeedIds.References.MythicalManMonth })).ShouldNotBeNullOrWhiteSpace();
        _fieldsUndo = body.GetProperty("undo_id").GetString()!;
    }

    [Test]
    [Order(2)]
    public async Task Undo_WhenTheFieldsUpdateIsUndone_ShouldPutTheFieldsBack()
    {
        var before = await ScalarAsync<string?>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth });
        before.ShouldBe("Reissue Press");

        await Client.OkAsync("undo", new { undo_id = _fieldsUndo });

        (await ScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldNotBe("Reissue Press");
        (await ScalarAsync<bool>("SELECT rights_holder IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBeTrue();
    }

    [Test]
    [Order(3)]
    public async Task UpdateSource_WhenCiteKeyAndPermissionsContactAreSet_ShouldWriteThemRecordTheHistoryAndUndoShouldPutThemBack()
    {
        var body = await Client.OkAsync("update_source", new
        {
            source = "#10", book = Book, fields = new { cite_key = "brooks1975", permissions_contact = "rights@example.test" },
        });

        (await ScalarAsync<string>("SELECT cite_key FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("brooks1975");
        (await ScalarAsync<string>("SELECT permissions_contact FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("rights@example.test");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'details_edited' ORDER BY created_at DESC LIMIT 1",
            new { id = SeedIds.References.MythicalManMonth })).ShouldBe("agent");
        (await Client.OkAsync("get_clearance", new { source = "brooks1975", book = Book })).GetProperty("focus").GetProperty("source").GetProperty("id").GetGuid()
            .ShouldBe(SeedIds.References.MythicalManMonth);

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await ScalarAsync<string>("SELECT cite_key FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("brooks1995");
        (await ScalarAsync<bool>("SELECT permissions_contact IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBeTrue();
    }

    [Test]
    [Order(4)]
    public async Task UpdateSource_WhenTheCiteKeyIsEditedAfterTheWrite_ShouldBeAConflictOnUndo()
    {
        var body = await Client.OkAsync("update_source", new { source = "#10", book = Book, fields = new { cite_key = "brooks1975" } });
        await ScalarAsync<int>("UPDATE reference SET cite_key = 'brooks75' WHERE id = @id RETURNING 1", new { id = SeedIds.References.MythicalManMonth });

        var error = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ScalarAsync<string>("SELECT cite_key FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe("brooks75");
        await ScalarAsync<int>("UPDATE reference SET cite_key = 'brooks1995' WHERE id = @id RETURNING 1", new { id = SeedIds.References.MythicalManMonth });
    }

    [Test]
    [Order(5)]
    public async Task UpdateSource_WhenTheCiteKeyIsTakenOrMalformed_ShouldBeInvalidAndWriteNothingElse()
    {
        await Client.OkAsync("update_source", new { source = "#1", book = Book, fields = new { cite_key = "taken1999" } });
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");

        (await Client.ErrorAsync("update_source", new { source = "#10", book = Book, fields = new { cite_key = "taken1999", publisher = "Nobody" } }))
            .GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("update_source", new { source = "#10", book = Book, fields = new { cite_key = "two words" } })).GetProperty("code").GetString().ShouldBe("invalid");

        (await ScalarAsync<string?>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldNotBe("Nobody");
        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
    }

    [Test]
    [Order(6)]
    public async Task UpdateSource_WhenTheRequestIsRefused_ShouldBeNotFoundInvalidAndWriteNothing()
    {
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");

        (await Client.ErrorAsync("update_source", new { source = "#99", book = Book, fields = new { title = "x" } })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("update_source", new { source = "#1", book = Book, fields = new { colour = "red" } })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("update_source", new { source = "#1", book = Book, fields = new { licence = "free-for-all" } })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("update_source", new { source = "#1", book = Book, merge_into = "#2" })).GetProperty("field").GetString().ShouldBe("merge_into");
        (await Client.ErrorAsync("update_source", new { source = "#1", book = Book })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("update_source", new { source = "#1", book = Book, recheck = new[] { "isbn13" } })).GetProperty("field").GetString().ShouldBe("recheck");

        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
    }

    [Test]
    [Order(7)]
    [TestCase("30/10/2026")]
    [TestCase("2026-10-30T00:00")]
    [TestCase("2026-10-30+07:00")]
    [TestCase("2026-02-30")]
    [TestCase(" 2026-10-19")]
    [TestCase("2026-10-19 ")]
    [TestCase("1899-12-31")]
    [TestCase("9999-12-31")]
    public async Task UpdateSource_WhenTheAccessedDateIsMalformedOrOutOfRange_ShouldBeInvalidAndWriteNothing(string accessedOn)
    {
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");

        (await Client.ErrorAsync("update_source", new { source = "#10", book = Book, fields = new { accessed_on = accessedOn } }))
            .GetProperty("code").GetString().ShouldBe("invalid");

        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
    }

    [Test]
    [Order(8)]
    public async Task UpdateSource_WhenTheAccessedDateIsTomorrow_ShouldBeInvalid()
    {
        var tomorrow = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(1).ToString("yyyy-MM-dd");

        (await Client.ErrorAsync("update_source", new { source = "#10", book = Book, fields = new { accessed_on = tomorrow } }))
            .GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(9)]
    public async Task UpdateSource_WhenTheAccessedDateIsToday_ShouldSaveIt()
    {
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

        await Client.OkAsync("update_source", new { source = "#10", book = Book, fields = new { accessed_on = today.ToString("yyyy-MM-dd") } });

        (await ScalarAsync<DateOnly>("SELECT accessed_on FROM reference WHERE id = @id", new { id = SeedIds.References.MythicalManMonth })).ShouldBe(today);
    }
}
