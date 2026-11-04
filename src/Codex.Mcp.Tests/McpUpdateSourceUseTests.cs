using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// update_source_use over stdio on the Product Engineering seed (#20 M1): the Ch 8 · Sc 1 use of The Velocity Trap (a quote,
// locator "p. 118", 342 of 300 words). One scenario on its own clone, in order: a use retyped, its locator set and cleared,
// the refusals, a rewrap onto new words (stale version, dry run, then the write) with its undo, and a use removed.
[TestFixture]
[CancelAfter(120000)]
public class McpUpdateSourceUseTests
{
    private const string NewWords = "Adding people to a late project slows it down, because the new people must be taught first.";
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
    private McpStdioServer _server = null!;
    private string _undo = "";
    private string _bodyBeforeRewrap = "";

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

    private Task<string> BodyAsync() => ScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch8Sc1 });

    private Task<int> VersionAsync() => ScalarAsync<int>("SELECT version FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch8Sc1 });

    private Task<string> KindAsync() => ScalarAsync<string>("SELECT kind FROM reference_use WHERE id = @id", new { id = Use });

    [Test]
    [Order(1)]
    public async Task UpdateSourceUse_WhenTheCh8QuoteBecomesAParaphrase_ShouldDropUnderTheLimitAndRecordTheHistoryAsTheAgent()
    {
        var body = await Client.OkAsync("update_source_use", new { use = Use, kind = "paraphrase" });

        body.GetProperty("status_before").GetString().ShouldBe("risk");
        body.GetProperty("status").GetString().ShouldBe("needs_check");
        body.GetProperty("reason").GetString().ShouldBe("214 of 300 words. Ready to sign off");
        body.GetProperty("quoted_words").GetInt32().ShouldBe(214);
        body.GetProperty("affected").EnumerateArray().Select(a => a.GetProperty("field").GetString()).ShouldContain("kind");
        (await KindAsync()).ShouldBe("paraphrase");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'use_retyped' ORDER BY created_at DESC LIMIT 1",
            new { id = SeedIds.References.VelocityTrap })).ShouldBe("agent");
        _undo = body.GetProperty("undo_id").GetString()!;
    }

    [Test]
    [Order(2)]
    public async Task Undo_WhenTheRetypeIsUndone_ShouldMakeTheUseAQuoteAgain()
    {
        await Client.OkAsync("undo", new { undo_id = _undo });

        (await KindAsync()).ShouldBe("quote");
        (await Client.OkAsync("get_clearance", new { source = "#4", book = Book })).GetProperty("focus").GetProperty("status").GetString().ShouldBe("risk");
    }

    [Test]
    [Order(3)]
    public async Task UpdateSourceUse_WhenTheLocatorIsSet_ShouldWriteItRecordTheHistoryAndBeUndoable()
    {
        var body = await Client.OkAsync("update_source_use", new { use = Use, locator = "pp. 118–119" });

        (await ScalarAsync<string>("SELECT locator FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe("pp. 118–119");
        body.GetProperty("locator").GetString().ShouldBe("pp. 118–119");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'use_locator_changed'", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe("agent");

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await ScalarAsync<string>("SELECT locator FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe("p. 118");
    }

    [Test]
    [Order(4)]
    public async Task UpdateSourceUse_WhenTheLocatorIsEmpty_ShouldClearItAndUndoShouldPutItBack()
    {
        var body = await Client.OkAsync("update_source_use", new { use = Use, locator = "" });

        (await ScalarAsync<bool>("SELECT locator IS NULL FROM reference_use WHERE id = @id", new { id = Use })).ShouldBeTrue();

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await ScalarAsync<string>("SELECT locator FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe("p. 118");
    }

    [Test]
    [Order(5)]
    public async Task UpdateSourceUse_WhenTheLocatorWasEditedSinceTheWrite_ShouldBeAConflictOnUndo()
    {
        var body = await Client.OkAsync("update_source_use", new { use = Use, locator = "p. 1" });
        await ScalarAsync<int>("UPDATE reference_use SET locator = 'p. 2' WHERE id = @id RETURNING 1", new { id = Use });

        var error = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ScalarAsync<string>("SELECT locator FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe("p. 2");
        await ScalarAsync<int>("UPDATE reference_use SET locator = 'p. 118' WHERE id = @id RETURNING 1", new { id = Use });
    }

    [Test]
    [Order(6)]
    public async Task UpdateSourceUse_WhenTheRequestIsRefused_ShouldBeNotFoundOrInvalidAndWriteNothing()
    {
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");
        var version = await VersionAsync();

        (await Client.ErrorAsync("update_source_use", new { use = Guid.NewGuid(), kind = "quote" })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("update_source_use", new { use = Use, kind = "essay" })).GetProperty("field").GetString().ShouldBe("kind");
        (await Client.ErrorAsync("update_source_use", new { use = Use })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("update_source_use", new { use = Use, remove = true, kind = "quote" })).GetProperty("field").GetString().ShouldBe("remove");
        (await Client.ErrorAsync("update_source_use", new { use = Use, rewrap = new { text = NewWords } })).GetProperty("field").GetString().ShouldBe("expected_version");
        (await Client.ErrorAsync("update_source_use", new { use = Use, locator = new string('x', 201) })).GetProperty("code").GetString().ShouldBe("invalid");

        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
        (await VersionAsync()).ShouldBe(version);
    }

    [Test]
    [Order(7)]
    public async Task UpdateSourceUse_WhenRewrapNamesWordsThatAreNotInTheScene_ShouldBeInvalidAndWriteNothing()
    {
        // The author (or an edit_prose / resolve_draft call) put the paraphrase into the scene.
        await ScalarAsync<int>("UPDATE prose SET body = rtrim(body, E'\\n') || E'\\n\\n' || @words || E'\\n', version = version + 1 WHERE scene_id = @id RETURNING 1",
            new { id = SeedIds.Scenes.Ch8Sc1, words = NewWords });
        _bodyBeforeRewrap = await BodyAsync();
        var version = await VersionAsync();

        var error = await Client.ErrorAsync("update_source_use", new { use = Use, rewrap = new { text = "words that are not in the scene" }, expected_version = version });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await BodyAsync()).ShouldBe(_bodyBeforeRewrap);
        (await VersionAsync()).ShouldBe(version);
    }

    [Test]
    [Order(8)]
    public async Task UpdateSourceUse_WhenRewrapHasAStaleVersion_ShouldConflictAndWriteNothing()
    {
        var version = await VersionAsync();

        var error = await Client.ErrorAsync("update_source_use", new { use = Use, rewrap = new { text = NewWords }, expected_version = version - 1 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await BodyAsync()).ShouldBe(_bodyBeforeRewrap);
        (await KindAsync()).ShouldBe("quote");
    }

    [Test]
    [Order(9)]
    public async Task UpdateSourceUse_WhenRewrapIsADryRun_ShouldReportAndWriteNothing()
    {
        var version = await VersionAsync();
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");

        var body = await Client.OkAsync("update_source_use",
            new { use = Use, rewrap = new { text = NewWords }, kind = "paraphrase", expected_version = version, dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("side_effects").EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.StartsWith("Would move the marker"));
        (await BodyAsync()).ShouldBe(_bodyBeforeRewrap);
        (await KindAsync()).ShouldBe("quote");
        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
        (await Client.ErrorAsync("update_source_use", new { use = Use, rewrap = new { text = NewWords }, expected_version = version - 1, dry_run = true }))
            .GetProperty("code").GetString().ShouldBe("conflict");
    }

    [Test]
    [Order(10)]
    public async Task UpdateSourceUse_WhenTheUseIsRewrappedOntoTheNewWords_ShouldKeepTheSameUseAndMoveItsMarker()
    {
        var version = await VersionAsync();

        var body = await Client.OkAsync("update_source_use",
            new { use = Use, rewrap = new { text = NewWords }, kind = "paraphrase", expected_version = version });

        var now = await BodyAsync();
        now.ShouldContain($"[{NewWords}](ref:{Use})");
        now.Split($"(ref:{Use})").Length.ShouldBe(2, "the use's marker is on the new words only");
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE scene_id = @id AND archived_at IS NULL AND id = @use",
            new { id = SeedIds.Scenes.Ch8Sc1, use = Use })).ShouldBe(1);
        (await KindAsync()).ShouldBe("paraphrase");
        (await ScalarAsync<string>("SELECT locator FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe("p. 118");
        (await ScalarAsync<string>("SELECT excerpt FROM reference_use WHERE id = @id", new { id = Use })).ShouldBe(NewWords);
        body.GetProperty("version").GetInt32().ShouldBe(await VersionAsync());
        body.GetProperty("status").GetString().ShouldBe("needs_check");
        _undo = body.GetProperty("undo_id").GetString()!;
    }

    [Test]
    [Order(11)]
    public async Task Undo_WhenTheRewrapIsUndone_ShouldPutTheProseAndTheKindBack()
    {
        await Client.OkAsync("undo", new { undo_id = _undo });

        (await BodyAsync()).ShouldBe(_bodyBeforeRewrap);
        (await KindAsync()).ShouldBe("quote");
        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM reference_use WHERE id = @id", new { id = Use })).ShouldBeTrue();
    }

    [Test]
    [Order(12)]
    public async Task UpdateSourceUse_WhenTheUseIsRemoved_ShouldUnwrapItsMarkerAndNotBeUndoable()
    {
        var body = await Client.OkAsync("update_source_use", new { use = Use, remove = true });

        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference_use WHERE id = @id", new { id = Use })).ShouldBeTrue();
        (await BodyAsync()).ShouldNotContain($"(ref:{Use})");
        body.GetProperty("version").GetInt32().ShouldBeGreaterThan(0);
        var undo = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });
        undo.GetProperty("code").GetString().ShouldBe("blocked");
    }

    [Test]
    [Order(13)]
    public async Task UpdateSourceUse_WhenTheUseIsArchivedAndNotRewrapped_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("update_source_use", new { use = Use, kind = "quote" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(14)]
    public async Task UpdateSourceUse_WhenAnArchivedUseIsRewrapped_ShouldBringItBackOnTheNewWords()
    {
        var version = await VersionAsync();

        await Client.OkAsync("update_source_use", new { use = Use, rewrap = new { text = NewWords }, expected_version = version });

        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM reference_use WHERE id = @id", new { id = Use })).ShouldBeTrue();
        (await BodyAsync()).ShouldContain($"[{NewWords}](ref:{Use})");
    }
}
