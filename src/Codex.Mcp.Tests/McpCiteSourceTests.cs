using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// cite_source over stdio on the Product Engineering seed. One scenario on its own clone, in order: a passage cited for an
// existing source (marker, clearance, undo), a stale version, a passage that is not there, a typed-in source (added, then
// archived by undo), an identifier that matches a source (including a Not cited one), the Velocity Trap flag cited from the
// inbox, and a dry run.
[TestFixture]
[CancelAfter(120000)]
public class McpCiteSourceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Scene = SeedIds.Scenes.Ch7Sc1.ToString();
    private McpStdioServer _server = null!;
    private string _undo = "";
    private string _bodyBefore = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>("SELECT body, version FROM prose WHERE scene_id = @scene", new { scene = SeedIds.Scenes.Ch7Sc1 });
    }

    private async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args) ?? throw new InvalidOperationException("no value");
    }

    // The first sentence-sized run of plain words in the scene with no marker in it: a passage the tool can wrap.
    private async Task<string> PassageAsync()
    {
        var (body, _) = await ProseAsync();
        var paragraph = body.Split("\n\n").First(p => !p.StartsWith("::") && !p.Contains("](ref:") && p.Split(' ').Length > 8);
        return string.Join(' ', paragraph.Split(' ').Take(6));
    }

    [Test]
    [Order(1)]
    public async Task CiteSource_WhenAPassageIsCitedForASource_ShouldMarkItAndReportTheClearance()
    {
        var (original, version) = await ProseAsync();
        _bodyBefore = original;
        var passage = await PassageAsync();
        var uses = await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL");

        var body = await Client.OkAsync("cite_source",
            new { scene = "7.1", book = Book, passage = new { text = passage }, source = "Accelerate", kind = "paraphrase", expected_version = version });

        body.GetProperty("marker").GetInt32().ShouldBe(8);
        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.Accelerate);
        body.GetProperty("status").GetString().ShouldBe("needs_check");
        body.GetProperty("source_how").GetString().ShouldBe("ref");
        body.GetProperty("version").GetInt32().ShouldBe(version + 1);
        body.GetProperty("side_effects").EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.Contains("since sign-off"));
        body.GetProperty("affected").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ShouldContain("source_use");
        _undo = body.GetProperty("undo_id").GetString()!;
        (await ProseAsync()).Body.ShouldContain($"](ref:{body.GetProperty("use").GetGuid()})");
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL")).ShouldBe(uses + 1);
        (await ScalarAsync<string>("SELECT source FROM prose_revision WHERE scene_id = @scene ORDER BY created_at DESC LIMIT 1", new { scene = SeedIds.Scenes.Ch7Sc1 })).ShouldBe("agent");
    }

    [Test]
    [Order(2)]
    public async Task CiteSource_WhenTheVersionIsStale_ShouldBeAConflictAndWriteNothing()
    {
        var (before, version) = await ProseAsync();

        var error = await Client.ErrorAsync("cite_source",
            new { scene = Scene, passage = new { text = "anything" }, source = "Accelerate", expected_version = version - 1 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ProseAsync()).Body.ShouldBe(before);
    }

    [Test]
    [Order(3)]
    public async Task CiteSource_WhenThePassageIsNotInTheScene_ShouldBeInvalidAndNotLeaveANewSourceBehind()
    {
        var (_, version) = await ProseAsync();
        var sources = await ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL");

        var error = await Client.ErrorAsync("cite_source", new
        {
            scene = Scene, passage = new { text = "words that are nowhere in this scene" }, expected_version = version,
            source = new { @new = new { title = "Orphan Book", kind = "book" } },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL")).ShouldBe(sources);
    }

    [Test]
    [Order(4)]
    public async Task CiteSource_WhenTheSourceIsUnknownOrTheArgumentsAreMissing_ShouldBeNotFoundOrInvalid()
    {
        var (_, version) = await ProseAsync();

        (await Client.ErrorAsync("cite_source", new { scene = Scene, passage = new { text = "x" }, source = "No Such Source", expected_version = version }))
            .GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("cite_source", new { scene = Scene, source = "Accelerate", expected_version = version }))
            .GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("cite_source", new { scene = Scene, passage = new { text = "x" }, source = "Accelerate", kind = "summary", expected_version = version }))
            .GetProperty("field").GetString().ShouldBe("kind");
    }

    [Test]
    [Order(5)]
    public async Task Undo_WhenTheCitationIsUndone_ShouldPutTheSceneBack()
    {
        await Client.OkAsync("undo", new { undo_id = _undo });

        (await ProseAsync()).Body.ShouldBe(_bodyBefore);
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE scene_id = @scene AND reference_id = @source AND archived_at IS NULL",
            new { scene = SeedIds.Scenes.Ch7Sc1, source = SeedIds.References.Accelerate })).ShouldBe(1);
    }

    [Test]
    [Order(6)]
    public async Task CiteSource_WhenTheSourceIsTypedIn_ShouldAddItCiteItAndArchiveItOnUndo()
    {
        var (_, version) = await ProseAsync();
        var passage = await PassageAsync();

        var body = await Client.OkAsync("cite_source", new
        {
            scene = Scene, passage = new { text = passage }, expected_version = version, kind = "data", locator = "Table 5.1",
            source = new { @new = new { kind = "book", title = "A Typed-In Book", authors = "Doe, Jane", year = 2020, publisher = "Pub" } },
        });

        body.GetProperty("source_how").GetString().ShouldBe("added");
        body.GetProperty("locator").GetString().ShouldBe("Table 5.1");
        var id = body.GetProperty("source").GetProperty("id").GetGuid();
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'added'", new { id })).ShouldBe("agent");

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @id", new { id })).ShouldBeTrue();
    }

    [Test]
    [Order(7)]
    public async Task CiteSource_WhenAnIdentifierMatchesANotCitedSource_ShouldUseThatSourceInsteadOfAddingOne()
    {
        // The seed's sources carry no ISBN (J9B's 9780321150783 matches nothing there), so give Lean Software Development one first.
        await Client.OkAsync("update_source", new { source = "Lean Software Development", fields = new { isbn = "9780321150783" } });
        var (_, version) = await ProseAsync();
        var passage = await PassageAsync();
        var sources = await ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL");

        var body = await Client.OkAsync("cite_source",
            new { scene = Scene, passage = new { text = passage }, expected_version = version, kind = "quote", source = new { isbn = "978-0-321-15078-3" } });

        body.GetProperty("source_how").GetString().ShouldBe("matched");
        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.LeanSoftware);
        body.GetProperty("status_before").GetString().ShouldBe("not_cited");
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL")).ShouldBe(sources);
    }

    [Test]
    [Order(8)]
    public async Task CiteSource_WhenAFlagIsCited_ShouldWrapItsWordsAndResolveTheFlag()
    {
        var error = await Client.ErrorAsync("cite_source", new { from_flag = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch, locator = "p. 999" });
        error.GetProperty("field").GetString().ShouldBe("locator");

        var body = await Client.OkAsync("cite_source",
            new { from_flag = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch, kind = "quote", locator = "p. 112", source = "#4" });

        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.VelocityTrap);
        body.GetProperty("status").GetString().ShouldBe("risk");
        body.GetProperty("reason").GetString().ShouldBe("376 of 300 words, no request");
        (await ScalarAsync<string>("SELECT status FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch })).ShouldBe("cited");
        var again = await Client.ErrorAsync("cite_source", new { from_flag = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch });
        again.GetProperty("code").GetString().ShouldBe("conflict");
        var undo = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });
        undo.GetProperty("code").GetString().ShouldBe("blocked");
    }

    [Test]
    [Order(9)]
    public async Task CiteSource_WhenTheFlagHasNoGuessAndNoSourceIsGiven_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("cite_source", new { from_flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("source");
    }

    [Test]
    [Order(10)]
    public async Task CiteSource_WhenItIsADryRun_ShouldWriteNothing()
    {
        var (before, version) = await ProseAsync();
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");

        var body = await Client.OkAsync("cite_source",
            new { scene = Scene, passage = new { text = "anything" }, expected_version = version, source = "Accelerate", dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        (await ProseAsync()).Body.ShouldBe(before);
        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
    }
}
